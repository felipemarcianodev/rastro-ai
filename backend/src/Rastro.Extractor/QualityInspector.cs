using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rastro.Core.Graph;

namespace Rastro.Extractor;

// Inspeção de qualidade dos métodos do fluxo (Fase 10). Sem IA: só o que o compilador mostra.
// Métricas por método e três famílias de achados:
//   métrica       complexidade, tamanho, aninhamento e dependências acima do limiar
//   código limpo  catch que engole a falha, service locator
//   DDD           entidade alterada fora do domínio, gravação pelo repositório fora do domínio,
//                 setter público na entidade
// As regras de DDD são heurísticas: "domínio" é o namespace com o segmento Domain/Dominio, e
// "entidade" é a classe do código com Id que mora no domínio ou herda de *Entity/*Document/*Aggregate*.
internal static class QualityInspector
{
    private static readonly string[] WriteVerbs =
        ["Add", "Update", "Remove", "Delete", "Insert", "Save", "Upsert", "Atualizar", "Adicionar", "Inserir", "Remover", "Excluir", "Salvar", "Gravar", "Incluir", "Alterar"];

    private static readonly HashSet<string> LocatorMethods =
        ["GetService", "GetRequiredService", "GetServices", "GetKeyedService", "GetRequiredKeyedService"];

    private const int MaxSettersListed = 5;

    public static async Task<FlowGraph> InspectAsync(FlowGraph graph, FlowWalker walker, CancellationToken ct)
    {
        var metrics = new Dictionary<string, MethodMetrics>(StringComparer.Ordinal);
        var findings = new List<Finding>();
        // Por nome completo: o mesmo tipo visto por projetos diferentes da solution são símbolos diferentes.
        var reportedTypes = new HashSet<string>(StringComparer.Ordinal);
        var touchedEntities = new Dictionary<string, (INamedTypeSymbol Type, string NodeId)>(StringComparer.Ordinal);

        foreach (var (nodeId, method) in walker.Methods)
        {
            ct.ThrowIfCancellationRequested();
            if (walker.BodyOf(method) is not { } body) continue;
            var model = await body.Document.GetSemanticModelAsync(ct);
            if (model is null) continue;

            var measured = new MethodMetrics(Cyclomatic(body.Node), Lines(body.Node), MaxNesting(body.Node), Dependencies(method.ContainingType));
            metrics[nodeId] = measured;

            findings.AddRange(MetricFindings(nodeId, method, measured, reportedTypes));
            findings.AddRange(CleanCodeFindings(nodeId, method, body.Node, model, walker));
            findings.AddRange(DddFindings(nodeId, method, body.Node, model, walker, touchedEntities));
        }

        foreach (var (entity, nodeId) in touchedEntities.Values)
        {
            var setters = PublicSetters(entity);
            if (setters.Count == 0) continue;
            var listed = string.Join(", ", setters.Take(MaxSettersListed)) + (setters.Count > MaxSettersListed ? ", …" : "");
            findings.Add(new Finding(FindingKind.PublicSetterOnEntity, FindingSeverity.Info, nodeId,
                $"{entity.Name} tem setter público em {setters.Count} propriedade(s) ({listed}): qualquer camada muda o estado sem passar pela regra da entidade.",
                walker.LocationOf(entity.Locations.FirstOrDefault(l => l.IsInSource))));
        }

        var order = graph.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(x => x.Id, x => x.i);
        return graph with
        {
            Nodes = graph.Nodes.Select(n => metrics.TryGetValue(n.Id, out var m) ? n with { Metrics = m } : n).ToList(),
            Findings = findings
                .OrderBy(f => order.GetValueOrDefault(f.NodeId, int.MaxValue))
                .ThenBy(f => f.Location?.Line ?? 0)
                .ThenBy(f => f.Kind)
                .ToList()
        };
    }

    // 1 + cada ponto de decisão: if, laços, catch, case, braço de switch, ?:, &&, || e ??.
    internal static int Cyclomatic(SyntaxNode body) =>
        1 + body.DescendantNodes().Count(n => n switch
        {
            IfStatementSyntax or WhileStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax or DoStatementSyntax
                or CatchClauseSyntax or ConditionalExpressionSyntax or CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax => true,
            SwitchExpressionArmSyntax arm => arm.Pattern is not DiscardPatternSyntax,
            BinaryExpressionSyntax b => b.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression or SyntaxKind.CoalesceExpression,
            _ => false
        });

    internal static int Lines(SyntaxNode body)
    {
        var span = body.GetLocation().GetLineSpan();
        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }

    // Maior número de blocos de controle um dentro do outro. "else if" fica no nível do if.
    internal static int MaxNesting(SyntaxNode body)
    {
        var max = 0;
        foreach (var node in body.DescendantNodes().Where(IsNesting))
        {
            var depth = 0;
            for (var current = node; current is not null && current != body; current = current.Parent)
            {
                if (current is IfStatementSyntax && current.Parent is ElseClauseSyntax { Parent: IfStatementSyntax })
                    continue;
                if (IsNesting(current)) depth++;
            }
            max = Math.Max(max, depth);
        }
        return max;
    }

    private static bool IsNesting(SyntaxNode node) =>
        node is IfStatementSyntax or WhileStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax or DoStatementSyntax
            or TryStatementSyntax or SwitchStatementSyntax or UsingStatementSyntax or LockStatementSyntax;

    // Só conta o que é dependência injetada: interface ou classe de serviço. Valor, texto, record e
    // entidade (construtor de entidade recebe dados, não dependências) ficam de fora.
    internal static int Dependencies(INamedTypeSymbol type) =>
        IsEntity(type) || type.IsRecord
            ? 0
            : type.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared)
                .Select(c => c.Parameters.Count(p => IsInjected(p.Type))).DefaultIfEmpty(0).Max();

    private static bool IsInjected(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Interface
        || type is INamedTypeSymbol { TypeKind: TypeKind.Class, SpecialType: SpecialType.None, IsRecord: false } named && !IsEntity(named)
            && named.ContainingNamespace?.ToDisplayString() is not ("System" or "System.IO");

    private static IEnumerable<Finding> MetricFindings(string nodeId, IMethodSymbol method, MethodMetrics m, HashSet<string> reportedTypes)
    {
        var name = $"{method.ContainingType.Name}.{method.Name}";
        if (m.Cyclomatic > QualityThresholds.ComplexityWarning)
            yield return new Finding(FindingKind.HighComplexity,
                m.Cyclomatic > QualityThresholds.ComplexityCritical ? FindingSeverity.Critical : FindingSeverity.Warning, nodeId,
                $"{name} tem complexidade ciclomática {m.Cyclomatic} (limite {QualityThresholds.ComplexityWarning}): caminhos demais para testar e entender.");
        if (m.Lines > QualityThresholds.LinesWarning)
            yield return new Finding(FindingKind.LongMethod,
                m.Lines > QualityThresholds.LinesCritical ? FindingSeverity.Critical : FindingSeverity.Warning, nodeId,
                $"{name} tem {m.Lines} linhas (limite {QualityThresholds.LinesWarning}): faz coisas demais.");
        if (m.MaxNesting > QualityThresholds.NestingWarning)
            yield return new Finding(FindingKind.DeepNesting, FindingSeverity.Warning, nodeId,
                $"{name} tem {m.MaxNesting} níveis de blocos um dentro do outro (limite {QualityThresholds.NestingWarning}).");
        if (m.Dependencies > QualityThresholds.DependenciesWarning && reportedTypes.Add(method.ContainingType.ToDisplayString()))
            yield return new Finding(FindingKind.TooManyDependencies, FindingSeverity.Warning, nodeId,
                $"{method.ContainingType.Name} recebe {m.Dependencies} dependências no construtor (limite {QualityThresholds.DependenciesWarning}): sinal de responsabilidades demais.");
    }

    private static IEnumerable<Finding> CleanCodeFindings(string nodeId, IMethodSymbol method, SyntaxNode body, SemanticModel model, FlowWalker walker)
    {
        foreach (var clause in body.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            var block = clause.Block;
            var caught = clause.Declaration?.Type.ToString() ?? "tudo";
            if (block.Statements.Count == 0)
                yield return new Finding(FindingKind.SilentCatch, FindingSeverity.Critical, nodeId,
                    $"catch ({caught}) vazio: a falha some sem log nem tratamento.", walker.LocationOf(clause.CatchKeyword.GetLocation()));
            // Traduzir a exceção (usar a variável dela num retorno de erro) não é engolir a falha.
            else if (!block.DescendantNodes().Any(n => n is ThrowStatementSyntax or ThrowExpressionSyntax)
                     && !block.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => InvokedName(i).Contains("Log", StringComparison.OrdinalIgnoreCase))
                     && !UsesException(clause))
                yield return new Finding(FindingKind.SilentCatch, FindingSeverity.Warning, nodeId,
                    $"catch ({caught}) sem log, sem relançar e sem usar a exceção: a causa da falha se perde.", walker.LocationOf(clause.CatchKeyword.GetLocation()));
        }

        if (IsCompositionRoot(method.ContainingType)) yield break;
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called || !LocatorMethods.Contains(called.Name)) continue;
            var owner = (called.ReducedFrom ?? called).ContainingType;
            var ns = owner.ContainingNamespace?.ToDisplayString() ?? "";
            if (owner.Name != "IServiceProvider" && !ns.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal)) continue;

            var hosted = IsHostedOrJob(method.ContainingType);
            yield return new Finding(FindingKind.ServiceLocator, hosted ? FindingSeverity.Info : FindingSeverity.Warning, nodeId,
                hosted
                    ? $"{method.ContainingType.Name} resolve dependência com {called.Name} (comum em job e serviço em segundo plano, que abrem escopo): confira se não dá para injetar."
                    : $"{method.ContainingType.Name} resolve dependência com {called.Name} (service locator): a dependência fica escondida do construtor.",
                walker.LocationOf(invocation.GetLocation()));
        }
    }

    private static IEnumerable<Finding> DddFindings(string nodeId, IMethodSymbol method, SyntaxNode body, SemanticModel model, FlowWalker walker,
        Dictionary<string, (INamedTypeSymbol Type, string NodeId)> touchedEntities)
    {
        var caller = method.ContainingType;
        // O próprio repositório (ou DbContext) é quem grava: não é ele que pula o domínio.
        if (IsDomainType(caller) || IsEntity(caller) || IsStore(caller)) yield break;
        var where = $"{caller.Name}.{method.Name}";
        var reported = new HashSet<string>(StringComparer.Ordinal);

        // Atribuição (ou ++/--) numa propriedade de entidade, fora da entidade e do domínio.
        var writes = body.DescendantNodes().Select(n => n switch
        {
            AssignmentExpressionSyntax a => a.Left,
            PostfixUnaryExpressionSyntax p when p.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression => p.Operand,
            PrefixUnaryExpressionSyntax p when p.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression => p.Operand,
            _ => null
        }).OfType<MemberAccessExpressionSyntax>();
        foreach (var target in writes)
        {
            var member = model.GetSymbolInfo(target).Symbol;
            if (member is not (IPropertySymbol or IFieldSymbol)) continue;
            if (model.GetTypeInfo(target.Expression).Type is not INamedTypeSymbol owner || !IsEntity(owner)) continue;
            touchedEntities.TryAdd(owner.ToDisplayString(), (owner, nodeId));
            if (!reported.Add($"set:{owner.Name}.{member.Name}")) continue;
            yield return new Finding(FindingKind.EntityChangedOutsideDomain, FindingSeverity.Warning, nodeId,
                $"{where} altera {owner.Name}.{member.Name} direto, fora da entidade e do serviço de domínio que a valida.",
                walker.LocationOf(target.GetLocation()));
        }

        // Gravação de entidade pelo repositório (ou DbSet) chamada de fora do domínio.
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called || !IsWriteVerb(called.Name)) continue;
            var store = called.ContainingType;
            if (!IsStore(store)) continue;
            var entity = invocation.ArgumentList.Arguments
                .Select(a => model.GetTypeInfo(a.Expression).Type).OfType<INamedTypeSymbol>().FirstOrDefault(IsEntity);
            if (entity is null) continue;
            touchedEntities.TryAdd(entity.ToDisplayString(), (entity, nodeId));
            if (!reported.Add($"store:{entity.Name}.{called.Name}")) continue;
            yield return new Finding(FindingKind.PersistenceOutsideDomain, FindingSeverity.Info, nodeId,
                $"{where} grava {entity.Name} com {store.Name}.{called.Name} direto, sem passar por um serviço de domínio que valide a entidade.",
                walker.LocationOf(invocation.GetLocation()));
        }
    }

    private static bool UsesException(CatchClauseSyntax clause) =>
        clause.Declaration?.Identifier.Text is { Length: > 0 } name
        && clause.Block.DescendantNodes().OfType<IdentifierNameSyntax>().Any(i => i.Identifier.Text == name);

    private static string InvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        IdentifierNameSyntax i => i.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        _ => invocation.Expression.ToString()
    };

    private static bool IsWriteVerb(string name) => WriteVerbs.Any(v => name.StartsWith(v, StringComparison.Ordinal));

    private static bool IsStore(INamedTypeSymbol type) =>
        type.Name.Contains("Repositor", StringComparison.OrdinalIgnoreCase)
        || type.AllInterfaces.Any(i => i.Name.Contains("Repositor", StringComparison.OrdinalIgnoreCase))
        || BaseNames(type).Any(n => n is "DbContext" or "DbSet");

    internal static bool IsDomainType(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return ns.Split('.').Any(part => part is "Domain" or "Dominio" or "Domínio");
    }

    internal static bool IsEntity(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class } named || named.SpecialType != SpecialType.None) return false;
        if (!named.Locations.Any(l => l.IsInSource)) return false;
        var looksLikeEntity = IsDomainType(named) || BaseNames(named).Skip(1).Any(n =>
            n.EndsWith("Entity", StringComparison.Ordinal) || n.EndsWith("Document", StringComparison.Ordinal) || n.Contains("Aggregate", StringComparison.Ordinal));
        return looksLikeEntity && Chain(named).Any(t => t.GetMembers("Id").OfType<IPropertySymbol>().Any());
    }

    // Só as propriedades da própria entidade: as da classe base (auditoria, Id) são do mecanismo
    // comum a todas e costumam ser preenchidas pela infraestrutura.
    private static List<string> PublicSetters(INamedTypeSymbol entity) =>
        entity.GetMembers().OfType<IPropertySymbol>()
            .Where(p => p.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false } && p.DeclaredAccessibility == Accessibility.Public)
            .Select(p => p.Name)
            .Distinct()
            .ToList();

    private static bool IsCompositionRoot(INamedTypeSymbol type) =>
        type.Name is "Program" or "Startup" || type.Name.EndsWith("Extensions", StringComparison.Ordinal);

    private static bool IsHostedOrJob(INamedTypeSymbol type) =>
        BaseNames(type).Any(n => n == "BackgroundService")
        || type.AllInterfaces.Any(i => i.Name is "IHostedService" or "IJob" or "IInvocable");

    private static IEnumerable<INamedTypeSymbol> Chain(INamedTypeSymbol type)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            yield return current;
    }

    private static IEnumerable<string> BaseNames(INamedTypeSymbol type) => Chain(type).Select(t => t.Name);
}
