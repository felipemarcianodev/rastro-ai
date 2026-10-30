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

    private const string HttpClientType = "System.Net.Http.HttpClient";
    private const string HttpClientJsonType = "System.Net.Http.Json.HttpClientJsonExtensions";

    private sealed record WalkContext(SemanticModel Model, int Depth, CancellationToken Ct);

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

        var ctx = new WalkContext(model, depth, ct);
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

        var method = Normalize(called);

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

        if (IsInSolution(method))
        {
            if (!NeedsImplementation(method))
            {
                var target = await WalkMethodAsync(method, ctx.Depth + 1, isEntry: false, ctx.Ct);
                graph.AddEdge(parent, target, label);
                return;
            }

            var implementations = await FindImplementationsAsync(method, ctx.Ct);
            switch (implementations.Count)
            {
                case 1:
                    var target = await WalkMethodAsync(implementations[0], ctx.Depth + 1, isEntry: false, ctx.Ct);
                    graph.AddEdge(parent, target, label);
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
            call = new HttpCall(verb, arguments.Count > 0 ? ResolveRoute(arguments[0].Expression, ctx) : null);
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

    private async Task<IReadOnlyList<IMethodSymbol>> FindImplementationsAsync(IMethodSymbol method, CancellationToken ct)
    {
        var definition = method.OriginalDefinition;
        var found = definition.ContainingType.TypeKind == TypeKind.Interface
            ? await SymbolFinder.FindImplementationsAsync(definition, solution, cancellationToken: ct)
            : await SymbolFinder.FindOverridesAsync(definition, solution, cancellationToken: ct);

        var implementations = found
            .OfType<IMethodSymbol>()
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

    private static IMethodSymbol Normalize(IMethodSymbol method) => (method.ReducedFrom ?? method).OriginalDefinition;

    private static string MethodId(IMethodSymbol method) =>
        "m:" + (method.GetDocumentationCommentId() ?? method.ToDisplayString());

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

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
