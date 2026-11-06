using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Rastro.Core.Graph;
using Rastro.Core.Http;

namespace Rastro.Extractor;

// Percorre um método e tudo o que ele chama, gerando o grafo de fluxo.
// Determinístico: só o que o compilador prova vira aresta "extracted"; o resto vira nó
// "unresolved" com o motivo (ADR-001).
internal sealed partial class FlowWalker(
    Solution solution,
    GraphBuilder graph,
    DiRegistrationIndex di,
    ExtractionOptions options,
    string baseDirectory)
{
    private const int MaxLabelLength = 120;

    // Chamadas externas a estes namespaces são ruído (LINQ, coleções, texto, log), não fronteira.
    private static readonly string[] IgnoredExternalNamespaces =
    [
        "System.Linq", "System.Collections", "System.Text", "System.Threading", "System.Diagnostics",
        "System.Globalization", "System.Runtime", "Microsoft.Extensions.Logging", "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Primitives"
    ];

    private static readonly HashSet<string> MessageDispatchTypes =
    [
        "IMediator", "ISender", "IPublisher", "IBus", "IMessageBus", "IEventBus", "IPublishEndpoint",
        "ISendEndpoint", "IDispatcher", "IMessageSession", "ICapPublisher"
    ];

    private static readonly string[] MessageDispatchNamespaces = ["MediatR", "MassTransit", "Wolverine", "Rebus", "NServiceBus"];

    // Métodos do HttpClient (e das extensões de System.Net.Http.Json) que viram chamada HTTP, com o verbo.
    // SendAsync fica à parte: o verbo vem do HttpRequestMessage.
    private static readonly Dictionary<string, string> HttpVerbs = new(StringComparer.Ordinal)
    {
        ["GetAsync"] = "GET", ["GetStringAsync"] = "GET", ["GetStreamAsync"] = "GET", ["GetByteArrayAsync"] = "GET",
        ["GetFromJsonAsync"] = "GET",
        ["PostAsync"] = "POST", ["PostAsJsonAsync"] = "POST",
        ["PutAsync"] = "PUT", ["PutAsJsonAsync"] = "PUT",
        ["PatchAsync"] = "PATCH", ["PatchAsJsonAsync"] = "PATCH",
        ["DeleteAsync"] = "DELETE", ["DeleteFromJsonAsync"] = "DELETE"
    };

    // Atributos do Refit (namespace Refit) e o verbo de cada um. Aceita o nome com ou sem o sufixo Attribute.
    private const string RefitNamespace = "Refit";
    private static readonly Dictionary<string, string> RefitVerbs = new(StringComparer.Ordinal)
    {
        ["Get"] = "GET", ["Post"] = "POST", ["Put"] = "PUT", ["Patch"] = "PATCH", ["Delete"] = "DELETE",
        ["Head"] = "HEAD", ["Options"] = "OPTIONS"
    };

    private const string HttpClientType = "System.Net.Http.HttpClient";
    private const string HttpClientJsonType = "System.Net.Http.Json.HttpClientJsonExtensions";

    // Closure: os parâmetros de tipo do tipo genérico do método percorrido e o tipo fechado de cada um
    // (EfRepository<Tarefa>: T vira Tarefa). Serve para fechar também as chamadas de dentro do método.
    private sealed record WalkContext(SemanticModel Model, int Depth, CancellationToken Ct, IReadOnlyList<(ITypeParameterSymbol Parameter, ITypeSymbol Argument)> Closure);

    // Chamada (arquivo e posição) -> nó do método que ela atende. A inspeção de banco usa para saber que
    // método uma chamada dentro de um laço, ou antes de um filtro, alcança.
    private readonly Dictionary<(string File, int Start), string> _callTargets = [];

    internal string? TargetOf(SyntaxNode call) =>
        _callTargets.TryGetValue((call.SyntaxTree.FilePath, call.SpanStart), out var id) ? id : null;

    private void RecordTarget(InvocationExpressionSyntax invocation, string target) =>
        _callTargets[(invocation.SyntaxTree.FilePath, invocation.SpanStart)] = target;

    private readonly Dictionary<string, IMethodSymbol> _methods = new(StringComparer.Ordinal);

    // Métodos percorridos (id do nó -> símbolo), para a inspeção de qualidade (Fase 10).
    public IReadOnlyDictionary<string, IMethodSymbol> Methods => _methods;

    private readonly List<HttpCallSite> _httpCalls = [];

    // Chamadas HTTP encontradas (só com options.HttpCalls), na ordem do percurso.
    public IReadOnlyList<HttpCallSite> HttpCalls => _httpCalls;

    internal (SyntaxNode Node, Document Document)? BodyOf(IMethodSymbol method) => FindBody(method);

    internal SourceLocation? LocationOf(Location? location) => ToLocation(location);

    public async Task<string> WalkMethodAsync(IMethodSymbol method, int depth, bool isEntry, CancellationToken ct)
    {
        method = Normalize(method);
        var id = MethodId(method);
        if (graph.HasNode(id)) return id;

        graph.AddNode(new FlowNode(
            id,
            isEntry ? NodeKind.Entry : NodeKind.Method,
            MethodLabel(method),
            method.ToDisplayString(),
            ToLocation(method.Locations.FirstOrDefault(l => l.IsInSource)),
            Project: method.ContainingAssembly?.Name));
        _methods[id] = method;

        var body = FindBody(method);
        if (body is null) return id;

        if (depth >= options.MaxDepth)
        {
            var limitId = "u:depth:" + id;
            graph.AddNode(new FlowNode(limitId, NodeKind.Unresolved, "limite de profundidade",
                Reason: UnresolvedReason.DepthLimit, Detail: $"MaxDepth = {options.MaxDepth}"));
            graph.AddEdge(id, limitId, null);
            return id;
        }

        var model = await body.Value.Document.GetSemanticModelAsync(ct);
        if (model is null) return id;

        var ctx = new WalkContext(model, depth, ct, ClosureOf(method));
        if (body.Value.Node is BlockSyntax block)
            await WalkStatementAsync(block, id, null, nested: false, ctx);
        else
            await WalkExpressionAsync(body.Value.Node, id, null, ctx);

        return id;
    }

    private async Task WalkStatementAsync(StatementSyntax statement, string parent, string? label, bool nested, WalkContext ctx)
    {
        switch (statement)
        {
            case BlockSyntax block:
                foreach (var inner in block.Statements)
                    await WalkStatementAsync(inner, parent, label, nested, ctx);
                break;

            case IfStatementSyntax ifStatement:
            {
                await WalkExpressionAsync(ifStatement.Condition, parent, label, ctx);
                var condition = Clean(ifStatement.Condition.ToString());
                var id = AddStructureNode("if", ifStatement, NodeKind.Decision, $"if ({condition})", parent, label, condition);
                await WalkStatementAsync(ifStatement.Statement, id, "then", true, ctx);
                if (ifStatement.Else is not null)
                    await WalkStatementAsync(ifStatement.Else.Statement, id, "else", true, ctx);
                break;
            }

            case SwitchStatementSyntax switchStatement:
            {
                await WalkExpressionAsync(switchStatement.Expression, parent, label, ctx);
                var expression = Clean(switchStatement.Expression.ToString());
                var id = AddStructureNode("switch", switchStatement, NodeKind.Decision, $"switch ({expression})", parent, label, expression);
                foreach (var section in switchStatement.Sections)
                {
                    var caseLabel = string.Join(" | ", section.Labels.Select(CaseLabel));
                    foreach (var inner in section.Statements)
                        await WalkStatementAsync(inner, id, caseLabel, true, ctx);
                }
                break;
            }

            case TryStatementSyntax tryStatement:
            {
                var id = AddStructureNode("try", tryStatement, NodeKind.Try, "try", parent, label);
                await WalkStatementAsync(tryStatement.Block, id, "try", true, ctx);
                foreach (var catchClause in tryStatement.Catches)
                {
                    var catchLabel = catchClause.Declaration is null ? "catch" : $"catch ({Clean(catchClause.Declaration.Type.ToString())})";
                    if (catchClause.Filter is not null)
                        catchLabel += $" when ({Clean(catchClause.Filter.FilterExpression.ToString())})";
                    await WalkStatementAsync(catchClause.Block, id, catchLabel, true, ctx);
                }
                if (tryStatement.Finally is not null)
                    await WalkStatementAsync(tryStatement.Finally.Block, id, "finally", true, ctx);
                break;
            }

            case CommonForEachStatementSyntax forEach:
            {
                await WalkExpressionAsync(forEach.Expression, parent, label, ctx);
                var header = forEach is ForEachStatementSyntax fe
                    ? $"foreach ({fe.Type} {fe.Identifier} in {fe.Expression})"
                    : $"foreach ({((ForEachVariableStatementSyntax)forEach).Variable} in {forEach.Expression})";
                var id = AddStructureNode("loop", forEach, NodeKind.Loop, Clean(header), parent, label, Clean(forEach.Expression.ToString()));
                await WalkStatementAsync(forEach.Statement, id, "each", true, ctx);
                break;
            }

            case ForStatementSyntax forStatement:
            {
                var condition = forStatement.Condition is null ? string.Empty : Clean(forStatement.Condition.ToString());
                var id = AddStructureNode("loop", forStatement, NodeKind.Loop, $"for ({condition})", parent, label, condition);
                await WalkStatementAsync(forStatement.Statement, id, "each", true, ctx);
                break;
            }

            case WhileStatementSyntax whileStatement:
            {
                await WalkExpressionAsync(whileStatement.Condition, parent, label, ctx);
                var condition = Clean(whileStatement.Condition.ToString());
                var id = AddStructureNode("loop", whileStatement, NodeKind.Loop, $"while ({condition})", parent, label, condition);
                await WalkStatementAsync(whileStatement.Statement, id, "each", true, ctx);
                break;
            }

            case DoStatementSyntax doStatement:
            {
                var condition = Clean(doStatement.Condition.ToString());
                var id = AddStructureNode("loop", doStatement, NodeKind.Loop, $"do … while ({condition})", parent, label, condition);
                await WalkStatementAsync(doStatement.Statement, id, "each", true, ctx);
                await WalkExpressionAsync(doStatement.Condition, id, "each", ctx);
                break;
            }

            case ReturnStatementSyntax returnStatement:
                if (returnStatement.Expression is not null)
                    await WalkExpressionAsync(returnStatement.Expression, parent, label, ctx);
                if (nested)
                {
                    var text = returnStatement.Expression is null ? "return" : $"return {Clean(returnStatement.Expression.ToString())}";
                    AddStructureNode("return", returnStatement, NodeKind.Return, text, parent, label);
                }
                break;

            case ThrowStatementSyntax throwStatement:
                if (throwStatement.Expression is not null)
                    await WalkExpressionAsync(throwStatement.Expression, parent, label, ctx);
                AddStructureNode("throw", throwStatement, NodeKind.Throw, ThrowLabel(throwStatement.Expression), parent, label);
                break;

            case UsingStatementSyntax usingStatement:
                if (usingStatement.Declaration is not null)
                    await WalkExpressionAsync(usingStatement.Declaration, parent, label, ctx);
                if (usingStatement.Expression is not null)
                    await WalkExpressionAsync(usingStatement.Expression, parent, label, ctx);
                await WalkStatementAsync(usingStatement.Statement, parent, label, nested, ctx);
                break;

            case LockStatementSyntax lockStatement:
                await WalkStatementAsync(lockStatement.Statement, parent, label, nested, ctx);
                break;

            case CheckedStatementSyntax checkedStatement:
                await WalkStatementAsync(checkedStatement.Block, parent, label, nested, ctx);
                break;

            case UnsafeStatementSyntax unsafeStatement:
                await WalkStatementAsync(unsafeStatement.Block, parent, label, nested, ctx);
                break;

            case FixedStatementSyntax fixedStatement:
                await WalkStatementAsync(fixedStatement.Statement, parent, label, nested, ctx);
                break;

            case LabeledStatementSyntax labeled:
                await WalkStatementAsync(labeled.Statement, parent, label, nested, ctx);
                break;

            case LocalFunctionStatementSyntax:
                // Só entra no fluxo quando é chamada.
                break;

            case ContinueStatementSyntax continueStatement:
                AddStructureNode("jump", continueStatement, NodeKind.Jump, "continue", parent, label);
                break;

            case BreakStatementSyntax breakStatement:
                // break de case de switch é só sintaxe; break de laço é desvio.
                if (breakStatement.Ancestors().FirstOrDefault(a => a is SwitchSectionSyntax or CommonForEachStatementSyntax
                        or ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax) is not SwitchSectionSyntax)
                    AddStructureNode("jump", breakStatement, NodeKind.Jump, "break", parent, label);
                break;

            default:
                await WalkExpressionAsync(statement, parent, label, ctx);
                break;
        }
    }

    // Chamadas dentro de uma expressão, na ordem de execução (a interna antes da externa).
    private async Task WalkExpressionAsync(SyntaxNode node, string parent, string? label, WalkContext ctx)
    {
        var invocations = node
            .DescendantNodesAndSelf(n => n is not LocalFunctionStatementSyntax)
            .OfType<InvocationExpressionSyntax>()
            .Where(i => i.Expression is not IdentifierNameSyntax { Identifier.ValueText: "nameof" })
            .OrderBy(i => i.Span.End)
            .ThenByDescending(i => i.SpanStart)
            .ToList();

        foreach (var invocation in invocations)
            await HandleInvocationAsync(invocation, parent, label, ctx);
    }

    private async Task HandleInvocationAsync(InvocationExpressionSyntax invocation, string parent, string? label, WalkContext ctx)
    {
        var info = ctx.Model.GetSymbolInfo(invocation, ctx.Ct);
        var symbol = info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null);
        var callText = Clean(invocation.Expression.ToString());

        if (symbol is not IMethodSymbol called)
        {
            AddUnresolved(invocation, UnresolvedReason.DynamicDispatch, callText, callText, parent, label);
            return;
        }

        if (called.MethodKind == MethodKind.DelegateInvoke)
        {
            AddUnresolved(invocation, UnresolvedReason.Delegate, callText, called.ContainingType.ToDisplayString(), parent, label);
            return;
        }

        var method = Close(Normalize(called), ctx);

        if (IsReflection(method))
        {
            AddUnresolved(invocation, UnresolvedReason.Reflection, callText, method.ToDisplayString(), parent, label);
            return;
        }

        if (IsMessageDispatch(method))
        {
            var message = invocation.ArgumentList.Arguments.FirstOrDefault() is { } argument
                ? ctx.Model.GetTypeInfo(argument.Expression, ctx.Ct).Type?.ToDisplayString()
                : null;
            AddUnresolved(invocation, UnresolvedReason.MessageDispatch, callText, message ?? method.ToDisplayString(), parent, label);
            return;
        }

        if (options.HttpCalls && TryHttpCall(invocation, method, callText, parent, label, ctx))
            return;

        // Interface do Refit: não tem implementação no código (o Refit gera em tempo de execução).
        if (TryRefitCall(invocation, method, callText, parent, label, ctx))
            return;

        if (IsInSolution(method))
        {
            if (!NeedsImplementation(method))
            {
                var target = await WalkMethodAsync(method, ctx.Depth + 1, isEntry: false, ctx.Ct);
                graph.AddEdge(parent, target, label);
                RecordTarget(invocation, target);
                return;
            }

            var receiver = invocation.Expression is MemberAccessExpressionSyntax access
                ? ctx.Model.GetTypeInfo(access.Expression, ctx.Ct).Type as INamedTypeSymbol
                : null;
            var implementations = await FindImplementationsAsync(method, receiver is null ? null : Substitute(receiver, ctx) as INamedTypeSymbol, ctx.Ct);
            switch (implementations.Count)
            {
                case 1:
                    var target = await WalkMethodAsync(implementations[0], ctx.Depth + 1, isEntry: false, ctx.Ct);
                    graph.AddEdge(parent, target, label);
                    RecordTarget(invocation, target);
                    break;
                case 0:
                    AddUnresolved(invocation, UnresolvedReason.NoImplementation, callText, method.ToDisplayString(), parent, label);
                    break;
                default:
                    var candidates = string.Join(", ", implementations.Select(i => i.ContainingType.ToDisplayString()));
                    AddUnresolved(invocation, UnresolvedReason.MultipleImplementations, callText, candidates, parent, label);
                    break;
            }
            return;
        }

        if (IsBoundaryCall(invocation, method, ctx))
        {
            var id = "x:" + (method.GetDocumentationCommentId() ?? method.ToDisplayString());
            graph.AddNode(new FlowNode(id, NodeKind.External, ExternalLabel(invocation, method, ctx), method.ToDisplayString(),
                Project: method.ContainingAssembly?.Name));
            graph.AddEdge(parent, id, label, ToLocation(invocation.GetLocation()));
        }
    }

    // Chamada do HttpClient: vira um nó próprio por local de chamada (h:arquivo:linha:coluna), com o
    // verbo e a rota que o compilador consegue ler. A costura entre sistemas troca o nó por uma aresta
    // até o método que atende; sem destino, o nó fica como "não resolvido" com o motivo.
    private bool TryHttpCall(InvocationExpressionSyntax invocation, IMethodSymbol method, string callText, string parent, string? label, WalkContext ctx)
    {
        var owner = method.ContainingType.ToDisplayString();
        var isClient = owner == HttpClientType;
        if (!isClient && owner != HttpClientJsonType) return false;

        var arguments = invocation.ArgumentList.Arguments;
        HttpCall call;
        if (isClient && method.Name == "SendAsync")
            call = ResolveSend(arguments, ctx);
        else if (HttpVerbs.TryGetValue(method.Name, out var verb))
        {
            var (fields, more) = ReadBodyFields(method.Name, arguments, ctx);
            call = new HttpCall(verb, arguments.Count > 0 ? ResolveRoute(arguments[0].Expression, ctx) : null, fields, more);
        }
        else
            return false;

        var location = ToLocation(invocation.GetLocation());
        var id = $"h:{location?.File}:{location?.Line}:{invocation.GetLocation().GetLineSpan().StartLinePosition.Character + 1}";
        var reason = call.Route is null ? UnresolvedReason.RouteNotResolved : UnresolvedReason.NoMatchingEndpoint;
        graph.AddNode(new FlowNode(id, NodeKind.Unresolved, Truncate($"{call.Method} {call.Route ?? "?"}"),
            Location: location, Reason: reason, Detail: Truncate(callText)));
        graph.AddEdge(parent, id, label, location, call);
        _httpCalls.Add(new HttpCallSite(id, call, ctx.Depth));
        return true;
    }

    // Método de interface do Refit ([Get("/rota")], [Post]...): vira a mesma chamada HTTP do HttpClient, com o
    // verbo e o template do atributo. Reconhece pelo símbolo do atributo (namespace Refit); se a dll não foi
    // resolvida, aceita o atributo sem símbolo quando o arquivo importa ou qualifica o namespace Refit.
    // Com as chamadas HTTP desligadas, vira um nó externo (nunca "sem implementação").
    private bool TryRefitCall(InvocationExpressionSyntax invocation, IMethodSymbol method, string callText, string parent, string? label, WalkContext ctx)
    {
        if (method.ContainingType.TypeKind != TypeKind.Interface) return false;
        var call = ReadRefitAttribute(method, out var template);
        if (call is null) return false;
        var (refitFields, refitMore) = ReadRefitFields(method, call.Method, template);
        call = call with { Fields = refitFields, MoreFields = refitMore };

        var location = ToLocation(invocation.GetLocation());
        if (!options.HttpCalls)
        {
            var externalId = "x:" + (method.GetDocumentationCommentId() ?? method.ToDisplayString());
            graph.AddNode(new FlowNode(externalId, NodeKind.External, $"{method.ContainingType.Name}.{method.Name}", method.ToDisplayString(),
                Project: method.ContainingAssembly?.Name));
            graph.AddEdge(parent, externalId, label, location);
            return true;
        }

        var id = $"h:{location?.File}:{location?.Line}:{invocation.GetLocation().GetLineSpan().StartLinePosition.Character + 1}";
        var reason = call.Route is null ? UnresolvedReason.RouteNotResolved : UnresolvedReason.NoMatchingEndpoint;
        graph.AddNode(new FlowNode(id, NodeKind.Unresolved, Truncate($"{call.Method} {call.Route ?? "?"}"),
            Location: location, Reason: reason, Detail: Truncate(callText)));
        graph.AddEdge(parent, id, label, location, call);
        _httpCalls.Add(new HttpCallSite(id, call, ctx.Depth));
        return true;
    }

    private static HttpCall? ReadRefitAttribute(IMethodSymbol method, out string? rawTemplate)
    {
        rawTemplate = null;
        foreach (var attribute in method.GetAttributes())
        {
            var syntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
            string? name;
            if (attribute.AttributeClass is { TypeKind: not TypeKind.Error } type)
            {
                if (type.ContainingNamespace?.ToDisplayString() != RefitNamespace) continue;
                name = type.Name;
            }
            else if (syntax is not null && ImportsRefit(syntax))
                name = syntax.Name.ToString().Split('.')[^1];
            else
                continue;

            if (name.EndsWith("Attribute", StringComparison.Ordinal)) name = name[..^"Attribute".Length];
            if (!RefitVerbs.TryGetValue(name, out var verb)) continue;

            var template = attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string text
                ? text
                : (syntax?.ArgumentList?.Arguments.FirstOrDefault()?.Expression as LiteralExpressionSyntax)?.Token.ValueText;
            rawTemplate = template;
            return new HttpCall(verb, template is null ? null : RefitRoute(template));
        }
        return null;
    }

    private static readonly HashSet<string?> RefitSkippedAttributes = new(StringComparer.Ordinal)
        { "Header", "HeaderCollection", "Authorize", "Property" };

    private static readonly HashSet<string> BodyMethods = new(StringComparer.Ordinal)
        { "PostAsync", "PutAsync", "PatchAsync", "PostAsJsonAsync", "PutAsJsonAsync", "PatchAsJsonAsync" };

    // Campos do corpo da chamada do HttpClient. As extensões *AsJsonAsync serializam o 2º argumento: os
    // campos são as propriedades do tipo dele. PostAsync/PutAsync/PatchAsync recebem um HttpContent: só
    // JsonContent.Create(x) é lido; null é "sem corpo"; qualquer outro conteúdo vira MoreFields.
    // Verbos sem corpo (GET, DELETE) não têm campos.
    private static (IReadOnlyList<string>? Fields, bool More) ReadBodyFields(string methodName, SeparatedSyntaxList<ArgumentSyntax> arguments, WalkContext ctx)
    {
        if (!BodyMethods.Contains(methodName) || arguments.Count < 2) return (null, false);

        var body = Unwrap(arguments[1].Expression);
        if (body.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression)) return (null, false);

        if (!methodName.EndsWith("AsJsonAsync", StringComparison.Ordinal))
        {
            if (body is not InvocationExpressionSyntax { ArgumentList.Arguments.Count: >= 1 } create
                || ctx.Model.GetSymbolInfo(create, ctx.Ct).Symbol is not IMethodSymbol { Name: "Create", ContainingType.Name: "JsonContent" })
                return (null, true);
            body = Unwrap(create.ArgumentList.Arguments[0].Expression);
        }

        var fields = PropertyNames(ctx.Model.GetTypeInfo(body, ctx.Ct).Type);
        return fields is { Count: > 0 } ? (fields, false) : (null, fields is null);
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
        return expression;
    }

    // Propriedades públicas de instância com getter, base antes da derivada. Nulo quando o tipo não tem
    // forma de objeto legível: object, dynamic, primitivo, enum, string, coleção, tipo sem resolver.
    private static List<string>? PropertyNames(ITypeSymbol? type)
    {
        if (type is null or IErrorTypeSymbol or IDynamicTypeSymbol or IArrayTypeSymbol or ITypeParameterSymbol) return null;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            return PropertyNames(nullable.TypeArguments[0]);
        if (type.SpecialType != SpecialType.None || type.TypeKind is TypeKind.Enum or TypeKind.Delegate or TypeKind.Pointer) return null;
        if (type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable)) return null;

        var chain = new List<ITypeSymbol>();
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            chain.Add(current);
        chain.Reverse();

        var names = new List<string>();
        foreach (var property in chain.SelectMany(t => t.GetMembers().OfType<IPropertySymbol>()))
        {
            if (property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public || property.GetMethod is not { DeclaredAccessibility: Accessibility.Public }) continue;
            if (!names.Contains(property.Name, StringComparer.Ordinal)) names.Add(property.Name);
        }
        return names;
    }

    // Campos de um método de interface do Refit: [Body] vira as propriedades do tipo; [Query] e o parâmetro
    // que preenche {nome} da rota entram pelo nome; CancellationToken e [Header] ficam fora. Sem atributo,
    // vale a regra do próprio Refit: em POST, PUT e PATCH o tipo complexo é o corpo; o resto vai para a
    // query pelo nome.
    private static (IReadOnlyList<string>? Fields, bool More) ReadRefitFields(IMethodSymbol method, string verb, string? template)
    {
        var routeNames = template is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : RefitPlaceholder().Matches(template).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasBody = verb is "POST" or "PUT" or "PATCH";

        var fields = new List<string>();
        var more = false;
        void Add(string name)
        {
            if (!fields.Contains(name, StringComparer.Ordinal)) fields.Add(name);
        }

        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken") continue;

            var attributes = parameter.GetAttributes().Select(RefitAttributeName).Where(n => n is not null).ToHashSet();
            if (attributes.Overlaps(RefitSkippedAttributes)) continue;

            var isBody = attributes.Contains("Body")
                || (hasBody && attributes.Count == 0 && !routeNames.Contains(parameter.Name) && PropertyNames(parameter.Type) is { Count: > 0 });
            if (isBody)
            {
                var names = PropertyNames(parameter.Type);
                if (names is null) more = true;
                else foreach (var name in names) Add(name);
                continue;
            }

            Add(parameter.Name);
        }
        return (fields.Count > 0 ? fields : null, more);
    }

    private static string? RefitAttributeName(AttributeData attribute)
    {
        string name;
        if (attribute.AttributeClass is { TypeKind: not TypeKind.Error } type)
        {
            if (type.ContainingNamespace?.ToDisplayString() != RefitNamespace) return null;
            name = type.Name;
        }
        else if (attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax syntax && ImportsRefit(syntax))
            name = syntax.Name.ToString().Split('.')[^1];
        else
            return null;

        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
    }

    private static bool ImportsRefit(AttributeSyntax syntax) =>
        syntax.Name.ToString().StartsWith(RefitNamespace + ".", StringComparison.Ordinal)
        || syntax.SyntaxTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Any(u => u.Name?.ToString() == RefitNamespace);

    // {id}, {**caminho} e o resto do template do Refit viram o buraco {}; endereço base, query e barras saem.
    private static string? RefitRoute(string template)
    {
        var route = RouteTemplate.Normalize(RefitParameter().Replace(template, RouteTemplate.Hole));
        return route.Length == 0 ? null : route;
    }

    // SendAsync(new HttpRequestMessage(HttpMethod.X, url)): só o formato inline é lido.
    // Qualquer outro (a requisição montada antes, em outra variável) fica como SEND sem rota.
    private static HttpCall ResolveSend(SeparatedSyntaxList<ArgumentSyntax> arguments, WalkContext ctx)
    {
        var unknown = new HttpCall("SEND", null);
        if (arguments.Count == 0 || arguments[0].Expression is not BaseObjectCreationExpressionSyntax creation)
            return unknown;

        var creationArguments = creation.ArgumentList?.Arguments;
        if (creationArguments is not { Count: >= 1 } list
            || list[0].Expression is not MemberAccessExpressionSyntax { Expression: var verbOwner, Name.Identifier.ValueText: var verbName }
            || ctx.Model.GetTypeInfo(verbOwner, ctx.Ct).Type?.ToDisplayString() != "System.Net.Http.HttpMethod")
            return unknown;

        var verb = verbName.ToUpperInvariant();
        if (verb is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE")) return unknown;

        return new HttpCall(verb, list.Count > 1 ? ResolveRoute(list[1].Expression, ctx) : null);
    }

    // Rota da chamada: literal, constante, interpolação ou concatenação. Pedaço não constante vira {};
    // os que aparecem antes do primeiro texto são o endereço base (vem de configuração) e saem.
    // Qualquer outra forma (variável, método, Uri) devolve nulo: a rota é montada em tempo de execução.
    private static string? ResolveRoute(ExpressionSyntax expression, WalkContext ctx)
    {
        var parts = new List<(string Text, bool IsHole)>();
        if (!CollectRoute(expression, ctx, parts, nested: false)) return null;

        var first = parts.FindIndex(p => !p.IsHole);
        if (first < 0) return null;

        var route = RouteTemplate.Normalize(string.Concat(parts.Skip(first).Select(p => p.IsHole ? RouteTemplate.Hole : p.Text)));
        return route.Length == 0 ? null : route;
    }

    private static bool CollectRoute(ExpressionSyntax expression, WalkContext ctx, List<(string Text, bool IsHole)> parts, bool nested)
    {
        var constant = ctx.Model.GetConstantValue(expression, ctx.Ct);
        if (constant.HasValue && constant.Value is string text)
        {
            parts.Add((text, false));
            return true;
        }

        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return CollectRoute(parenthesized.Expression, ctx, parts, nested);

            case InterpolatedStringExpressionSyntax interpolated:
                foreach (var content in interpolated.Contents)
                {
                    if (content is InterpolatedStringTextSyntax literal)
                    {
                        parts.Add((literal.TextToken.ValueText, false));
                        continue;
                    }

                    var hole = ((InterpolationSyntax)content).Expression;
                    var value = ctx.Model.GetConstantValue(hole, ctx.Ct);
                    if (value.HasValue && value.Value is not null)
                        parts.Add((Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, false));
                    else
                        parts.Add((string.Empty, true));
                }
                return true;

            case BinaryExpressionSyntax binary when binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddExpression):
                return CollectRoute(binary.Left, ctx, parts, nested: true) && CollectRoute(binary.Right, ctx, parts, nested: true);

            default:
                // Só dentro de uma concatenação um pedaço qualquer vira buraco; sozinho, a rota é desconhecida.
                if (!nested) return false;
                parts.Add((string.Empty, true));
                return true;
        }
    }

    private async Task<IReadOnlyList<IMethodSymbol>> FindImplementationsAsync(IMethodSymbol method, INamedTypeSymbol? receiver, CancellationToken ct)
    {
        var definition = method.OriginalDefinition;
        IEnumerable<IMethodSymbol> found;
        if (definition.ContainingType.TypeKind == TypeKind.Interface)
        {
            // Pelas regras do C#: cada tipo concreto que implementa a interface diz qual membro a implementa
            // (inclusive o herdado de uma base genérica já fechada, e o que esconde o da base com "new").
            var byType = await ImplementationsByTypeAsync(method, receiver, ct);
            found = byType.Count > 0
                ? byType
                : (await SymbolFinder.FindImplementationsAsync(definition, solution, cancellationToken: ct)).OfType<IMethodSymbol>();
        }
        else
            found = (await SymbolFinder.FindOverridesAsync(definition, solution, cancellationToken: ct)).OfType<IMethodSymbol>();

        var implementations = found
            .Select(Normalize)
            .Where(m => !m.IsAbstract && IsInSolution(m))
            .GroupBy(MethodId)
            .Select(g => g.First())
            .OrderBy(MethodId, StringComparer.Ordinal)
            .ToList();

        if (implementations.Count <= 1) return implementations;

        // Fakes e stubs de projetos de teste não competem com a implementação de produção.
        var production = implementations.Where(m => !IsInTestProject(m)).ToList();
        if (production.Count > 0) implementations = production;
        if (implementations.Count <= 1) return implementations;

        // Mais de uma: o registro de DI decide, quando existe.
        var registered = di.ImplementationsOf(definition.ContainingType);
        var narrowed = implementations
            .Where(m => registered.Contains(m.ContainingType.OriginalDefinition))
            .ToList();

        return narrowed.Count > 0 ? narrowed : implementations;
    }

    // Os membros que implementam a interface chamada, um por tipo concreto da solution que a implementa.
    // O tipo genérico aberto (EfRepository<T>) é fechado pelos argumentos da interface chamada
    // (IRepository<Tarefa> -> EfRepository<Tarefa>); o que não implementa essa interface fechada sai.
    private async Task<List<IMethodSymbol>> ImplementationsByTypeAsync(IMethodSymbol method, INamedTypeSymbol? receiver, CancellationToken ct)
    {
        var iface = method.ContainingType;
        var result = new List<IMethodSymbol>();
        var types = await SymbolFinder.FindImplementationsAsync(iface.OriginalDefinition, solution, cancellationToken: ct);
        foreach (var type in types.OfType<INamedTypeSymbol>().Where(t => t.TypeKind is TypeKind.Class or TypeKind.Struct))
        {
            var concrete = CloseImplementer(type.OriginalDefinition, iface);
            if (concrete is null || !concrete.AllInterfaces.Contains(iface, SymbolEqualityComparer.Default)) continue;
            // O membro herdado (IRepository<T>.GetAllAsync) chamado por um receptor mais específico
            // (ITarefaRepository): só entram os tipos que implementam o tipo do receptor.
            if (receiver is { TypeKind: TypeKind.Interface } && !SymbolEqualityComparer.Default.Equals(receiver, iface)
                && !concrete.AllInterfaces.Contains(receiver, SymbolEqualityComparer.Default)) continue;
            if (concrete.FindImplementationForInterfaceMember(method) is IMethodSymbol implementation)
                result.Add(implementation);
        }
        return result;
    }

    private static INamedTypeSymbol? CloseImplementer(INamedTypeSymbol type, INamedTypeSymbol iface)
    {
        if (!type.IsGenericType) return type;
        var declared = type.AllInterfaces.FirstOrDefault(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iface.OriginalDefinition));
        if (declared is null) return null;

        var arguments = new ITypeSymbol?[type.TypeParameters.Length];
        for (var i = 0; i < declared.TypeArguments.Length; i++)
            if (declared.TypeArguments[i] is ITypeParameterSymbol parameter && parameter.Ordinal < arguments.Length)
                arguments[parameter.Ordinal] = iface.TypeArguments[i];
        return arguments.Any(a => a is null) ? null : type.Construct(arguments!);
    }

    private static IReadOnlyList<(ITypeParameterSymbol, ITypeSymbol)> ClosureOf(IMethodSymbol method)
    {
        var owner = method.ContainingType;
        if (owner is null || !owner.IsGenericType) return [];
        var closure = new List<(ITypeParameterSymbol, ITypeSymbol)>();
        for (var i = 0; i < owner.TypeArguments.Length; i++)
            if (owner.TypeArguments[i] is not ITypeParameterSymbol)
                closure.Add((owner.OriginalDefinition.TypeParameters[i], owner.TypeArguments[i]));
        return closure;
    }

    // O método chamado dentro de um tipo genérico já fechado: _repo.GetByIdAsync de dentro de
    // EfRepository<Tarefa> chama EfRepository<Tarefa>.GetByIdAsync, não o da definição aberta.
    private static IMethodSymbol Close(IMethodSymbol method, WalkContext ctx)
    {
        var owner = method.ContainingType;
        if (ctx.Closure.Count == 0 || owner is null || !owner.IsGenericType) return method;
        var arguments = owner.TypeArguments.Select(a => Substitute(a, ctx)).ToArray();
        if (arguments.SequenceEqual(owner.TypeArguments, SymbolEqualityComparer.Default)) return method;

        var closed = owner.OriginalDefinition.Construct(arguments);
        return closed.GetMembers(method.Name).OfType<IMethodSymbol>()
            .FirstOrDefault(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, method.OriginalDefinition)) ?? method;
    }

    private static ITypeSymbol Substitute(ITypeSymbol type, WalkContext ctx)
    {
        switch (type)
        {
            case ITypeParameterSymbol parameter:
                foreach (var (p, argument) in ctx.Closure)
                    if (SymbolEqualityComparer.Default.Equals(p, parameter)) return argument;
                return type;
            case INamedTypeSymbol { IsGenericType: true } named:
                var arguments = named.TypeArguments.Select(a => Substitute(a, ctx)).ToArray();
                return arguments.SequenceEqual(named.TypeArguments, SymbolEqualityComparer.Default) ? type : named.OriginalDefinition.Construct(arguments);
            default:
                return type;
        }
    }

    private readonly Dictionary<ProjectId, bool> _testProjects = [];

    private bool IsInTestProject(IMethodSymbol method)
    {
        var project = method.DeclaringSyntaxReferences
            .Select(r => solution.GetDocument(r.SyntaxTree)?.Project)
            .FirstOrDefault(p => p is not null);
        if (project is null) return false;

        if (!_testProjects.TryGetValue(project.Id, out var isTest))
            _testProjects[project.Id] = isTest = TestProjectDetector.IsTestProject(project);
        return isTest;
    }

    private bool IsInSolution(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.Any(r => solution.GetDocument(r.SyntaxTree) is not null);

    private static bool NeedsImplementation(IMethodSymbol method) =>
        method.ContainingType.TypeKind == TypeKind.Interface || method.IsAbstract;

    private (SyntaxNode Node, Document Document)? FindBody(IMethodSymbol method)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var document = solution.GetDocument(reference.SyntaxTree);
            if (document is null) continue;

            SyntaxNode? body = reference.GetSyntax() switch
            {
                BaseMethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody?.Expression,
                LocalFunctionStatementSyntax f => (SyntaxNode?)f.Body ?? f.ExpressionBody?.Expression,
                AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody?.Expression,
                _ => null
            };
            if (body is not null) return (body, document);
        }
        return null;
    }

    private bool IsBoundaryCall(InvocationExpressionSyntax invocation, IMethodSymbol method, WalkContext ctx)
    {
        var ns = method.ContainingType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns == "System" || IgnoredExternalNamespaces.Any(p => ns == p || ns.StartsWith(p + ".", StringComparison.Ordinal)))
            return false;

        var root = ReceiverRoot(invocation);
        if (root is null) return false;

        return ctx.Model.GetSymbolInfo(root, ctx.Ct).Symbol switch
        {
            IFieldSymbol { IsStatic: false } => true,
            IPropertySymbol { IsStatic: false } => true,
            IParameterSymbol => true,
            ILocalSymbol => true,
            _ => false
        };
    }

    // Raiz da cadeia: em _db.Items.Where(x).ToListAsync(), devolve _db.
    private static ExpressionSyntax? ReceiverRoot(InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax? current = invocation.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Expression,
            MemberBindingExpressionSyntax => invocation.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>()?.Expression,
            _ => null
        };

        while (current is not null)
        {
            switch (current)
            {
                case InvocationExpressionSyntax i: current = i.Expression; break;
                case MemberAccessExpressionSyntax m: current = m.Expression; break;
                case ConditionalAccessExpressionSyntax c: current = c.Expression; break;
                case ParenthesizedExpressionSyntax p: current = p.Expression; break;
                case AwaitExpressionSyntax a: current = a.Expression; break;
                case PostfixUnaryExpressionSyntax u: current = u.Operand; break;
                case ElementAccessExpressionSyntax e: current = e.Expression; break;
                default: return current;
            }
        }
        return null;
    }

    private static string ExternalLabel(InvocationExpressionSyntax invocation, IMethodSymbol method, WalkContext ctx)
    {
        var receiver = invocation.Expression is MemberAccessExpressionSyntax m ? m.Expression : null;
        var receiverType = receiver is null ? null : ctx.Model.GetTypeInfo(receiver, ctx.Ct).Type?.Name;
        var owner = method.IsExtensionMethod && !string.IsNullOrEmpty(receiverType) ? receiverType : method.ContainingType.Name;
        return $"{owner}.{method.Name}";
    }

    private static bool IsReflection(IMethodSymbol method)
    {
        var type = method.ContainingType.ToDisplayString();
        return (method.Name is "Invoke" && type is "System.Reflection.MethodBase" or "System.Reflection.MethodInfo" or "System.Reflection.ConstructorInfo")
            || (method.Name is "CreateInstance" && type is "System.Activator")
            || (method.Name is "InvokeMember" && type is "System.Type");
    }

    private static bool IsMessageDispatch(IMethodSymbol method)
    {
        if (method.Name is not ("Send" or "SendAsync" or "Publish" or "PublishAsync" or "Dispatch" or "DispatchAsync"))
            return false;

        var type = method.ContainingType;
        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return MessageDispatchTypes.Contains(type.Name)
            || MessageDispatchNamespaces.Any(p => ns == p || ns.StartsWith(p + ".", StringComparison.Ordinal));
    }

    private string AddStructureNode(string prefix, SyntaxNode syntax, NodeKind kind, string label, string parent, string? edgeLabel, string? condition = null)
    {
        var location = ToLocation(syntax.GetLocation());
        var id = $"{prefix}:{location?.File}:{location?.Line}:{syntax.GetLocation().GetLineSpan().StartLinePosition.Character + 1}";
        graph.AddNode(new FlowNode(id, kind, Truncate(label), Location: location, Condition: condition is null ? null : Truncate(condition)));
        graph.AddEdge(parent, id, edgeLabel);
        return id;
    }

    private void AddUnresolved(InvocationExpressionSyntax invocation, UnresolvedReason reason, string label, string detail, string parent, string? edgeLabel)
    {
        var location = ToLocation(invocation.GetLocation());
        var id = $"u:{location?.File}:{location?.Line}:{invocation.GetLocation().GetLineSpan().StartLinePosition.Character + 1}";
        graph.AddNode(new FlowNode(id, NodeKind.Unresolved, Truncate(label), Location: location, Reason: reason, Detail: Truncate(detail)));
        graph.AddEdge(parent, id, edgeLabel);
    }

    private SourceLocation? ToLocation(Location? location)
    {
        if (location is null || !location.IsInSource) return null;
        var span = location.GetLineSpan();
        var path = span.Path;
        var relative = string.IsNullOrEmpty(baseDirectory) ? path : Path.GetRelativePath(baseDirectory, path);
        return new SourceLocation(relative.Replace('\\', '/'), span.StartLinePosition.Line + 1);
    }

    // Tira os argumentos do método genérico, mas guarda os do tipo (EfRepository<Tarefa>.GetByIdAsync).
    private static IMethodSymbol Normalize(IMethodSymbol method) => (method.ReducedFrom ?? method).ConstructedFrom;

    // O id é o da definição; um tipo genérico fechado leva os argumentos, para cada fechamento
    // (EfRepository<Tarefa>, EfRepository<Cliente>) ser um passo seu, com a sua tabela.
    private static string MethodId(IMethodSymbol method) =>
        "m:" + (method.OriginalDefinition.GetDocumentationCommentId() ?? method.OriginalDefinition.ToDisplayString()) + ClosureSuffix(method);

    private static string ClosureSuffix(IMethodSymbol method)
    {
        var owner = method.ContainingType;
        if (owner is null || !owner.IsGenericType || owner.TypeArguments.All(a => a is ITypeParameterSymbol)) return string.Empty;
        return "<" + string.Join(",", owner.TypeArguments.Select(a => a.ToDisplayString())) + ">";
    }

    private static string MethodLabel(IMethodSymbol method) =>
        method.MethodKind == MethodKind.LocalFunction && method.ContainingSymbol is IMethodSymbol outer
            ? $"{outer.ContainingType.Name}.{outer.Name}.{method.Name}"
            : $"{method.ContainingType.Name}.{method.Name}";

    private static string CaseLabel(SwitchLabelSyntax label) => label switch
    {
        DefaultSwitchLabelSyntax => "default",
        CaseSwitchLabelSyntax c => $"case {Clean(c.Value.ToString())}",
        CasePatternSwitchLabelSyntax p => $"case {Clean(p.Pattern.ToString())}" + (p.WhenClause is null ? string.Empty : $" when {Clean(p.WhenClause.Condition.ToString())}"),
        _ => Clean(label.ToString().TrimEnd(':'))
    };

    private static string ThrowLabel(ExpressionSyntax? expression) => expression switch
    {
        null => "throw",
        ObjectCreationExpressionSyntax creation => $"throw new {creation.Type}",
        _ => $"throw {Clean(expression.ToString())}"
    };

    private static string Clean(string text) => Whitespace().Replace(text, " ").Trim();

    private static string Truncate(string text) =>
        text.Length <= MaxLabelLength ? text : text[..(MaxLabelLength - 1)] + "…";

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex RefitParameter();

    [GeneratedRegex(@"\{\*{0,2}([A-Za-z_]\w*)")]
    private static partial Regex RefitPlaceholder();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
