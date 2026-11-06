using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rastro.Core.Database;
using Rastro.Core.Graph;
using Rastro.Extractor.Database;

namespace Rastro.Extractor;

// Banco nos fluxos (Fase 39). Sem IA: só o que o compilador mostra do EF Core em cada método do fluxo.
//   - o que o passo lê e grava: o DbSet (ou Set<T>) e o método do EF da cadeia (consulta, inclusão,
//     alteração, exclusão) e o Add, Update e Remove do próprio DbContext. A tabela sai do esquema lido
//     do código (Fase 37); sem ele, fica o nome do DbSet.
//   - procedure por SQL cru: FromSql, ExecuteSql e SqlQuery com um texto que começa por EXEC.
//   - achados: consulta dentro de laço (N+1), ToList antes do filtro, leitura sem AsNoTracking e a
//     procedure chamada por SQL cru.
// Os nomes são os do EF Core (Microsoft.EntityFrameworkCore); outro acesso a banco (Dapper, ADO.NET)
// continua só com a etiqueta "banco" da tela, sem tabela.
internal static class DbAccessInspector
{
    private static readonly HashSet<string> Inserts = new(StringComparer.Ordinal) { "Add", "AddAsync", "AddRange", "AddRangeAsync", "Attach", "AttachRange" };
    private static readonly HashSet<string> Updates = new(StringComparer.Ordinal) { "Update", "UpdateRange", "ExecuteUpdate", "ExecuteUpdateAsync" };
    private static readonly HashSet<string> Deletes = new(StringComparer.Ordinal) { "Remove", "RemoveRange", "ExecuteDelete", "ExecuteDeleteAsync" };
    private static readonly HashSet<string> Materializers = new(StringComparer.Ordinal) { "ToList", "ToListAsync", "ToArray", "ToArrayAsync", "AsEnumerable" };
    private static readonly HashSet<string> Filters = new(StringComparer.Ordinal)
        { "Where", "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault", "Any", "Count", "Skip", "Take", "OrderBy", "OrderByDescending" };
    // Terminais que devolvem entidades (as que o EF rastreia).
    private static readonly HashSet<string> EntityTerminals = new(StringComparer.Ordinal)
    {
        "ToList", "ToListAsync", "ToArray", "ToArrayAsync", "AsEnumerable", "First", "FirstAsync", "FirstOrDefault", "FirstOrDefaultAsync",
        "Single", "SingleAsync", "SingleOrDefault", "SingleOrDefaultAsync", "Last", "LastAsync", "LastOrDefault", "LastOrDefaultAsync", "Find", "FindAsync"
    };
    private static readonly HashSet<string> NoTracking = new(StringComparer.Ordinal) { "AsNoTracking", "AsNoTrackingWithIdentityResolution" };
    private static readonly HashSet<string> RawQueries = new(StringComparer.Ordinal) { "FromSql", "FromSqlRaw", "FromSqlInterpolated" };
    private static readonly HashSet<string> RawCommands = new(StringComparer.Ordinal)
        { "ExecuteSql", "ExecuteSqlRaw", "ExecuteSqlInterpolated", "ExecuteSqlAsync", "ExecuteSqlRawAsync", "ExecuteSqlInterpolatedAsync", "SqlQuery", "SqlQueryRaw" };
    private static readonly HashSet<string> SaveMethods = new(StringComparer.Ordinal) { "SaveChanges", "SaveChangesAsync" };

    private static readonly Regex Exec = new(@"^\s*EXEC(?:UTE)?\s+(?:@\w+\s*=\s*)?([\[\]\w\.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static async Task<FlowGraph> InspectAsync(FlowGraph graph, FlowWalker walker, Solution solution, string baseDirectory, DbSchema? repositorySchema, CancellationToken ct)
    {
        var scans = new Dictionary<string, MethodScan>(StringComparer.Ordinal);
        foreach (var (nodeId, method) in walker.Methods)
        {
            ct.ThrowIfCancellationRequested();
            if (walker.BodyOf(method) is not { } body) continue;
            var model = await body.Document.GetSemanticModelAsync(ct);
            if (model is null) continue;
            var scan = Scan(method, body.Node, model, ct);
            if (scan.Hits.Count > 0 || scan.LoopCalls.Count > 0) scans[nodeId] = scan;
        }
        if (!scans.Values.Any(s => s.Hits.Count > 0)) return graph;

        var schema = repositorySchema ?? await SchemaAsync(solution, baseDirectory, ct);
        var tables = schema.Tables.Where(t => t.Entity is not null)
            .GroupBy(t => t.Entity!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => DbNames.Display(g.First().Schema, g.First().Name, schema.Dialect), StringComparer.Ordinal);
        var procedures = schema.Procedures.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string TableOf(Hit hit) => hit.Entity is not null && tables.TryGetValue(hit.Entity, out var t) ? t : hit.SetName ?? hit.Entity ?? "?";

        var accesses = scans.ToDictionary(p => p.Key, p => p.Value.Hits
            .Select(h => new DbAccess(h.Procedure ?? TableOf(h), h.Operation, Line(h.At), h.Entity))
            .GroupBy(a => (a.Target, a.Operation))
            .Select(g => g.OrderBy(a => a.Line).First())
            .OrderBy(a => a.Line)
            .ToList(), StringComparer.Ordinal);

        var findings = new List<Finding>();
        foreach (var (nodeId, scan) in scans)
        {
            var name = $"{scan.Method.ContainingType.Name}.{scan.Method.Name}";
            foreach (var hit in scan.Hits.Where(h => h.InLoop && h.Operation == DbOperation.Read))
                findings.Add(new Finding(FindingKind.QueryInLoop, FindingSeverity.Warning, nodeId,
                    $"{name} consulta {TableOf(hit)} dentro de um laço: uma ida ao banco a cada volta (N+1). Traga os dados antes do laço, com Include ou com um Where que use Contains.",
                    walker.LocationOf(hit.At.GetLocation()), TableOf(hit)));

            foreach (var (call, callee) in scan.LoopCalls)
            {
                var target = walker.Methods.FirstOrDefault(m => Same(m.Value, callee));
                if (target.Key is null || target.Key == nodeId || !accesses.TryGetValue(target.Key, out var reads)) continue;
                var read = reads.Where(a => a.Operation == DbOperation.Read).Select(a => a.Target).Distinct().ToList();
                foreach (var table in read)
                    findings.Add(new Finding(FindingKind.QueryInLoop, FindingSeverity.Warning, nodeId,
                        $"{name} chama {callee.ContainingType.Name}.{callee.Name} dentro de um laço, e ele consulta {table}: uma ida ao banco a cada volta (N+1). Traga os dados antes do laço.",
                        walker.LocationOf(call.GetLocation()), table));
            }

            foreach (var hit in scan.Hits.Where(h => h.LateFilter is not null))
                findings.Add(new Finding(FindingKind.MaterializeBeforeFilter, FindingSeverity.Warning, nodeId,
                    $"{name} traz {TableOf(hit)} com {hit.LateFilter!.Value.Materializer} e só depois aplica {hit.LateFilter.Value.Filter}: o filtro roda na memória, depois de ler a tabela. Ponha o {hit.LateFilter.Value.Filter} antes do {hit.LateFilter.Value.Materializer}.",
                    walker.LocationOf(hit.LateFilter.Value.At.GetLocation()), TableOf(hit)));

            if (!scan.Writes && scan.Hits.All(h => h.Operation is DbOperation.Read))
                foreach (var hit in scan.Hits.Where(h => h.TrackedRead).DistinctBy(TableOf))
                    findings.Add(new Finding(FindingKind.ReadWithoutNoTracking, FindingSeverity.Info, nodeId,
                        $"{name} só lê {TableOf(hit)} e não pede AsNoTracking: o EF rastreia entidades que este método não grava. Se a leitura não alimenta uma gravação, use AsNoTracking.",
                        walker.LocationOf(hit.At.GetLocation()), TableOf(hit)));

            foreach (var hit in scan.Hits.Where(h => h.Procedure is not null))
            {
                var missing = procedures.Contains(hit.Procedure!) ? string.Empty : " Ela não aparece nos arquivos .sql do repositório.";
                findings.Add(new Finding(FindingKind.ProcedureByRawSql, FindingSeverity.Info, nodeId,
                    $"{name} chama a procedure {hit.Procedure} por {hit.RawMethod}: o compilador não confere o nome nem os parâmetros.{missing}",
                    walker.LocationOf(hit.At.GetLocation()), hit.Procedure));
            }
        }

        var order = graph.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(x => x.Id, x => x.i);
        return graph with
        {
            Nodes = graph.Nodes.Select(n => accesses.TryGetValue(n.Id, out var list) && list.Count > 0 ? n with { Db = list } : n).ToList(),
            Findings = (graph.Findings ?? []).Concat(findings)
                .OrderBy(f => order.GetValueOrDefault(f.NodeId, int.MaxValue))
                .ThenBy(f => f.Location?.Line ?? 0)
                .ThenBy(f => f.Kind)
                .ToList()
        };
    }

    private static MethodScan Scan(IMethodSymbol method, SyntaxNode body, SemanticModel model, CancellationToken ct)
    {
        var scan = new MethodScan(method);
        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case ExpressionSyntax expr when expr is not SimpleNameSyntax || expr.Parent is not MemberAccessExpressionSyntax { Name: var n } || n != expr:
                    if (DbSetEntity(model.GetTypeInfo(expr, ct).Type) is { } entity && !IsInsideDbSetExpression(expr, model, ct))
                        if (FromDbSet(expr, entity, body, model, ct) is { } hit) scan.Hits.Add(hit);
                    if (expr is InvocationExpressionSyntax call && Symbol(call, model, ct) is { } symbol)
                        Invocation(scan, call, symbol, body, model, ct);
                    break;
            }
        }
        return scan;
    }

    // Chamada fora da cadeia de um DbSet: Add/Update/Remove do DbContext, SQL cru do Database, SaveChanges
    // e a chamada a outro método dentro de um laço (para o N+1 por método).
    private static void Invocation(MethodScan scan, InvocationExpressionSyntax call, IMethodSymbol symbol, SyntaxNode body, SemanticModel model, CancellationToken ct)
    {
        var onContext = DerivesFrom(symbol.ContainingType, "DbContext");
        if (onContext && SaveMethods.Contains(symbol.Name))
        {
            scan.Writes = true;
            return;
        }
        if (onContext && Operation([symbol.Name]) is { } op and not DbOperation.Read && EntityArgument(symbol, call, model, ct) is { } entity)
        {
            scan.Writes = true;
            scan.Hits.Add(new Hit(entity, null, null, null, op, call, InLoop(call, body), false, null));
            return;
        }
        if (RawCommands.Contains(symbol.Name) && IsEf(symbol) && ProcedureOf(call) is { } procedure)
        {
            scan.Hits.Add(new Hit(null, null, procedure, symbol.Name, DbOperation.Execute, call, InLoop(call, body), false, null));
            return;
        }
        if (InLoop(call, body) && !IsEf(symbol) && symbol.MethodKind == MethodKind.Ordinary)
            scan.LoopCalls.Add((call, symbol));
    }

    private static Hit? FromDbSet(ExpressionSyntax expr, string entity, SyntaxNode body, SemanticModel model, CancellationToken ct)
    {
        var setName = expr switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            _ => null
        };
        var (chain, end) = Chain(expr);
        var names = chain.Select(c => c.Name).ToList();
        var iterated = end.Parent is CommonForEachStatementSyntax loop && loop.Expression == end;
        if (names.Count == 0 && !iterated) return null;

        var op = Operation(names) ?? DbOperation.Read;
        string? procedure = null, rawMethod = null;
        var raw = chain.FirstOrDefault(c => RawQueries.Contains(c.Name));
        if (raw.Call is not null && ProcedureOf(raw.Call) is { } proc)
        {
            procedure = proc;
            rawMethod = raw.Name;
            op = DbOperation.Execute;
        }

        var tracked = op == DbOperation.Read && !names.Any(NoTracking.Contains) && !names.Contains("Select")
            && (iterated || names.Any(EntityTerminals.Contains));
        return new Hit(entity, setName, procedure, rawMethod, op, expr, InLoop(expr, body), tracked, LateFilter(chain, end, body));
    }

    // A cadeia de chamadas que sai do DbSet, atravessando await e parênteses: "Where", "ToListAsync", ...
    private static (List<(string Name, InvocationExpressionSyntax Call)> Chain, ExpressionSyntax End) Chain(ExpressionSyntax start)
    {
        var chain = new List<(string, InvocationExpressionSyntax)>();
        ExpressionSyntax current = start;
        while (true)
        {
            var parent = current.Parent;
            if (parent is MemberAccessExpressionSyntax ma && ma.Expression == current && ma.Parent is InvocationExpressionSyntax inv && inv.Expression == ma)
            {
                chain.Add((ma.Name.Identifier.ValueText, inv));
                current = inv;
            }
            else if (parent is AwaitExpressionSyntax or ParenthesizedExpressionSyntax)
                current = (ExpressionSyntax)parent;
            else
                return (chain, current);
        }
    }

    // ToList (ou ToArray, AsEnumerable) seguido de um filtro, na mesma cadeia ou na variável que guarda a lista.
    private static (string Materializer, string Filter, SyntaxNode At)? LateFilter(List<(string Name, InvocationExpressionSyntax Call)> chain, ExpressionSyntax end, SyntaxNode body)
    {
        var at = chain.FindIndex(c => Materializers.Contains(c.Name));
        if (at < 0) return null;
        var materializer = chain[at].Name;
        var filter = chain.Skip(at + 1).FirstOrDefault(c => Filters.Contains(c.Name));
        if (filter.Call is not null) return (materializer, filter.Name, filter.Call);
        if (at != chain.Count - 1 || end.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable }) return null;

        var local = variable.Identifier.ValueText;
        var use = body.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(m => m.SpanStart > variable.Span.End && m.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == local && Filters.Contains(m.Name.Identifier.ValueText))
            .OrderBy(m => m.SpanStart)
            .FirstOrDefault();
        return use is null ? null : (materializer, use.Name.Identifier.ValueText, use);
    }

    private static DbOperation? Operation(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (Inserts.Contains(name)) return DbOperation.Insert;
            if (Updates.Contains(name)) return DbOperation.Update;
            if (Deletes.Contains(name)) return DbOperation.Delete;
        }
        return null;
    }

    // Só o corpo do laço repete: a coleção do foreach e a condição do for rodam uma vez (ou são o próprio laço).
    private static bool InLoop(SyntaxNode node, SyntaxNode body)
    {
        for (var current = node; current is not null && current != body; current = current.Parent)
        {
            var statement = current.Parent switch
            {
                CommonForEachStatementSyntax f => f.Statement,
                ForStatementSyntax f => f.Statement,
                WhileStatementSyntax w => w.Statement,
                DoStatementSyntax d => d.Statement,
                _ => null
            };
            if (statement == current) return true;
            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax) return false;
        }
        return false;
    }

    // "EXEC dbo.sp_baixar_estoque @p0" -> "sp_baixar_estoque"; com outro schema, "vendas.sp_x".
    private static string? ProcedureOf(InvocationExpressionSyntax call)
    {
        var argument = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        var text = argument switch
        {
            LiteralExpressionSyntax literal => literal.Token.ValueText,
            InterpolatedStringExpressionSyntax interpolated => string.Concat(interpolated.Contents.Select(c => c is InterpolatedStringTextSyntax t ? t.TextToken.ValueText : "{}")),
            _ => null
        };
        if (text is null || Exec.Match(text) is not { Success: true } match) return null;
        var parts = match.Groups[1].Value.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim('[', ']')).ToList();
        if (parts.Count == 0) return null;
        return parts.Count > 1 && !parts[^2].Equals("dbo", StringComparison.OrdinalIgnoreCase) ? $"{parts[^2]}.{parts[^1]}" : parts[^1];
    }

    private static string? DbSetEntity(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "DbSet", TypeArguments.Length: 1 } set ? set.TypeArguments[0].Name : null;

    // "_db.Pedidos" é o DbSet; o "_db.Set<Pedido>" dentro de "_db.Set<Pedido>()" não conta duas vezes.
    private static bool IsInsideDbSetExpression(ExpressionSyntax expr, SemanticModel model, CancellationToken ct) =>
        expr.Parent is InvocationExpressionSyntax inv && inv.Expression == expr && DbSetEntity(model.GetTypeInfo(inv, ct).Type) is not null;

    private static string? EntityArgument(IMethodSymbol symbol, InvocationExpressionSyntax call, SemanticModel model, CancellationToken ct)
    {
        if (symbol.IsGenericMethod && symbol.TypeArguments.Length == 1 && symbol.TypeArguments[0] is INamedTypeSymbol { SpecialType: SpecialType.None } t && t.Name != "Object")
            return t.Name;
        var argument = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        var type = argument is null ? null : model.GetTypeInfo(argument, ct).Type;
        return type switch
        {
            IArrayTypeSymbol array => array.ElementType.Name,
            INamedTypeSymbol { TypeArguments.Length: 1 } generic when symbol.Name.EndsWith("Range", StringComparison.Ordinal) => generic.TypeArguments[0].Name,
            INamedTypeSymbol { SpecialType: SpecialType.None } named when named.Name != "Object" => named.Name,
            _ => null
        };
    }

    private static IMethodSymbol? Symbol(InvocationExpressionSyntax call, SemanticModel model, CancellationToken ct)
    {
        var info = model.GetSymbolInfo(call, ct);
        return (info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) as IMethodSymbol;
    }

    private static bool IsEf(IMethodSymbol symbol) =>
        symbol.ContainingNamespace?.ToDisplayString().StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true;

    private static bool DerivesFrom(INamedTypeSymbol? type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.Name == name) return true;
        return false;
    }

    // O método chamado no laço e o nó do fluxo: o mesmo método, ou a implementação da interface chamada.
    private static bool Same(IMethodSymbol node, IMethodSymbol called)
    {
        if (node.OriginalDefinition.ToDisplayString() == called.OriginalDefinition.ToDisplayString()) return true;
        if (called.ContainingType.TypeKind != TypeKind.Interface) return false;
        return node.ContainingType.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == called.ContainingType.OriginalDefinition.ToDisplayString())
            && node.Name == called.Name && node.Parameters.Length == called.Parameters.Length;
    }

    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    // Sem o esquema do repositório: o dos documentos da solution, para dar o nome da tabela de cada entidade.
    private static async Task<DbSchema> SchemaAsync(Solution solution, string baseDirectory, CancellationToken ct)
    {
        var files = new List<SchemaFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in solution.Projects.SelectMany(p => p.Documents))
        {
            if (document.FilePath is not { } path || !seen.Add(path)) continue;
            var text = await document.GetTextAsync(ct);
            var relative = baseDirectory.Length > 0 ? Path.GetRelativePath(baseDirectory, path) : path;
            files.Add(new SchemaFile(relative.Replace('\\', '/'), text.ToString()));
        }
        return DbSchemaExtractor.Extract(files);
    }

    private sealed record Hit(
        string? Entity,
        string? SetName,
        string? Procedure,
        string? RawMethod,
        DbOperation Operation,
        SyntaxNode At,
        bool InLoop,
        bool TrackedRead,
        (string Materializer, string Filter, SyntaxNode At)? LateFilter);

    private sealed class MethodScan(IMethodSymbol method)
    {
        public IMethodSymbol Method { get; } = method;
        public List<Hit> Hits { get; } = [];
        public List<(InvocationExpressionSyntax Call, IMethodSymbol Callee)> LoopCalls { get; } = [];
        public bool Writes { get; set; }
    }
}
