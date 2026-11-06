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

    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
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
            if (scan.Hits.Count > 0 || scan.Calls.Count > 0 || scan.FilteredCalls.Count > 0 || scan.LeadingLikes.Count > 0) scans[nodeId] = scan;
        }
        if (!scans.Values.Any(s => s.Hits.Count > 0)) return graph;

        var schema = repositorySchema ?? await SchemaAsync(solution, baseDirectory, ct);
        var tables = schema.Tables.Where(t => t.Entity is not null)
            .GroupBy(t => t.Entity!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => DbNames.Display(g.First().Schema, g.First().Name, schema.Dialect), StringComparer.Ordinal);
        var procedures = schema.Procedures.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tableByEntity = schema.Tables.Where(t => t.Entity is not null)
            .GroupBy(t => t.Entity!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        string TableOf(Hit hit) => hit.Entity is not null && tables.TryGetValue(hit.Entity, out var t) ? t : hit.SetName ?? hit.Entity ?? "?";

        var accesses = scans.ToDictionary(p => p.Key, p => p.Value.Hits
            .Select(h => new DbAccess(h.Procedure ?? TableOf(h), h.Operation, Line(h.At), h.Entity, h.Sql))
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

            foreach (var call in scan.LoopCalls)
            {
                // O método que a chamada alcança é o que o percurso do fluxo resolveu (a implementação da
                // interface, já com o tipo genérico fechado), não o que o nome da chamada sugere. Se ele só
                // repassa a chamada a outro (serviço fino), a cadeia segue até o método que consulta (Fase 43c).
                if (walker.TargetOf(call) is not { } targetId || targetId == nodeId || !walker.Methods.TryGetValue(targetId, out var callee)) continue;
                var reached = Reach(targetId, nodeId, walker, scans, s => s.Calls,
                    id => accesses.TryGetValue(id, out var list) && list.Any(a => a.Operation == DbOperation.Read));
                var reported = new HashSet<string>(StringComparer.Ordinal);
                foreach (var path in reached)
                    foreach (var table in accesses[path[^1]].Where(a => a.Operation == DbOperation.Read).Select(a => a.Target).Distinct())
                    {
                        if (!reported.Add(table)) continue;
                        var leaf = walker.Methods[path[^1]];
                        findings.Add(new Finding(FindingKind.QueryInLoop, FindingSeverity.Warning, nodeId,
                            $"{name} chama {callee.ContainingType.Name}.{callee.Name} dentro de um laço, e ele{(path.Count == 1 ? string.Empty : $" chega a {leaf.ContainingType.Name}.{leaf.Name}, que")} consulta {table}: uma ida ao banco a cada volta (N+1). Traga os dados antes do laço.",
                            walker.LocationOf(call.GetLocation()), table));
                    }
            }

            // ToList só para apagar (Fase 43b): o aviso é o do ExecuteDelete, não o do filtro "Count" da lista.
            foreach (var (hit, removal) in scan.LoadToDelete)
                findings.Add(new Finding(FindingKind.LoadToDelete, FindingSeverity.Warning, nodeId,
                    $"{name} traz {TableOf(hit)} com {hit.Materializer} só para apagar com {removal.Name}: o EF lê cada linha e depois manda um DELETE por linha. Use ExecuteDeleteAsync sobre o Where (ele devolve quantas linhas apagou) e apaga numa instrução só.",
                    walker.LocationOf(removal.Call.GetLocation()), TableOf(hit)));

            foreach (var hit in scan.Hits.Where(h => h.LateFilter is not null && !(h.LateFilter.Value.Filter == "Count" && scan.LoadToDelete.Any(l => l.Hit == h))))
                findings.Add(new Finding(FindingKind.MaterializeBeforeFilter, FindingSeverity.Warning, nodeId,
                    $"{name} traz {TableOf(hit)} com {hit.LateFilter!.Value.Materializer} e só depois aplica {hit.LateFilter.Value.Filter}: o filtro roda na memória, depois de ler a tabela. Ponha o {hit.LateFilter.Value.Filter} antes do {hit.LateFilter.Value.Materializer}.",
                    walker.LocationOf(hit.LateFilter.Value.At.GetLocation()), TableOf(hit)));

            // O repositório devolve a lista já materializada e o chamador filtra na memória (Fase 43b).
            foreach (var (call, filter, at) in scan.FilteredCalls)
            {
                // A lista pode vir de um serviço fino que só devolve o que o repositório devolveu (Fase 43c):
                // a cadeia de retornos diretos segue até o método que materializa.
                if (walker.TargetOf(call) is not { } targetId || targetId == nodeId || !walker.Methods.TryGetValue(targetId, out var callee)) continue;
                var reached = Reach(targetId, nodeId, walker, scans, s => s.Returned,
                    id => scans.TryGetValue(id, out var found) && found.Hits.Any(h => h.Operation == DbOperation.Read && h.ReturnsMaterializedBy is not null));
                var reported = new HashSet<string>(StringComparer.Ordinal);
                foreach (var path in reached)
                {
                    var leaf = walker.Methods[path[^1]];
                    foreach (var source in scans[path[^1]].Hits.Where(h => h.Operation == DbOperation.Read && h.ReturnsMaterializedBy is not null).DistinctBy(TableOf))
                    {
                        if (!reported.Add(TableOf(source))) continue;
                        var origin = path.Count == 1 ? "que já a trouxe" : $"que repassa o resultado de {leaf.ContainingType.Name}.{leaf.Name}, e este já a trouxe";
                        findings.Add(new Finding(FindingKind.MaterializeBeforeFilter, FindingSeverity.Warning, nodeId,
                            $"{name} recebe {TableOf(source)} de {callee.ContainingType.Name}.{callee.Name}, {origin} com {source.ReturnsMaterializedBy} (a tabela inteira, sem este filtro), e só depois aplica {filter}: o filtro roda na memória. Passe o critério para {leaf.ContainingType.Name}.{leaf.Name} filtrar antes do {source.ReturnsMaterializedBy}.",
                            walker.LocationOf(at.GetLocation()), TableOf(source)));
                    }
                }
            }

            foreach (var (like, pattern, property) in scan.LeadingLikes)
                if (LikeTable(like, scan) is { } likeHit
                    && !HasTrigramIndex(likeHit.Entity is not null && tableByEntity.TryGetValue(likeHit.Entity, out var likeTable) ? likeTable : null, property))
                    findings.Add(new Finding(FindingKind.LeadingWildcardLike, FindingSeverity.Info, nodeId,
                        $"{name} busca em {TableOf(likeHit)} com um LIKE que começa por curinga ({pattern}): o banco não usa índice B-tree e lê a tabela inteira. Se a busca é por prefixo, tire o % do começo; no meio do texto, use índice trigram (PostgreSQL) ou busca de texto completo.",
                        walker.LocationOf(like.GetLocation()), TableOf(likeHit)));

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

    // Quantos saltos a cadeia de chamadas segue a partir do método que a chamada alcança (serviço fino).
    private const int MaxChainHops = 4;

    // Os caminhos (do método alcançado até o que atende a condição) mais curtos que chegam a um método que atende
    // a condição, seguindo as chamadas de edges. Anda em largura, nunca volta a um método já visto (recursão não
    // gira sem fim) e para depois de MaxChainHops saltos.
    private static List<List<string>> Reach(string start, string origin, FlowWalker walker, Dictionary<string, MethodScan> scans,
        Func<MethodScan, IEnumerable<InvocationExpressionSyntax>> edges, Func<string, bool> found)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { origin, start };
        var frontier = new List<List<string>> { new() { start } };
        for (var hop = 0; hop <= MaxChainHops && frontier.Count > 0; hop++)
        {
            var hits = frontier.Where(p => found(p[^1])).ToList();
            if (hits.Count > 0) return hits;
            var next = new List<List<string>>();
            foreach (var path in frontier)
            {
                if (!scans.TryGetValue(path[^1], out var scan)) continue;
                foreach (var call in edges(scan))
                    if (walker.TargetOf(call) is { } target && visited.Add(target))
                        next.Add([.. path, target]);
            }
            frontier = next;
        }
        return [];
    }

    private static MethodScan Scan(IMethodSymbol method, SyntaxNode body, SemanticModel model, CancellationToken ct)
    {
        var scan = new MethodScan(method);
        // Com o próprio corpo: num método de expressão (=> _repo.GetAsync(id)) a chamada é a raiz do corpo.
        foreach (var node in body.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case ExpressionSyntax expr when expr is not SimpleNameSyntax || expr.Parent is not MemberAccessExpressionSyntax { Name: var n } || n != expr:
                    // A entidade de um DbSet<T> dentro de um repositório genérico é o T da chamada (Closed); sem
                    // T fechado não há tabela a dizer, e o passo fica sem acesso em vez de inventar um nome.
                    if (DbSetEntity(model.GetTypeInfo(expr, ct).Type) is { } entityType && !IsInsideDbSetExpression(expr, model, ct)
                        && Closed(entityType, method) is { } entity)
                        if (FromDbSet(expr, entity.Name, body, model, ct) is { } hit) scan.Hits.Add(hit);
                    if (expr is InvocationExpressionSyntax call && Symbol(call, model, ct) is { } symbol)
                        Invocation(scan, call, symbol, body, model, ct);
                    break;
            }
        }

        // Carregar para alterar: o FindAsync que o repositório faz antes de gravar a mesma entidade é parte
        // da alteração, não uma consulta (nem conta no N+1 do laço que chama o repositório).
        var written = scan.Hits.Where(h => h.Operation != DbOperation.Read).Select(h => h.Entity).ToHashSet(StringComparer.Ordinal);
        scan.Hits.RemoveAll(h => h.IsFind && h.Operation == DbOperation.Read && written.Contains(h.Entity));

        foreach (var hit in scan.Hits.Where(h => h.Operation == DbOperation.Read && h.MaterializedInto is not null))
            if (RemovalOnly(hit, body, model, ct) is { } removal) scan.LoadToDelete.Add((hit, removal));
        return scan;
    }

    // ToList, RemoveRange e (no máximo) o tamanho da lista: a lista só serve para apagar. Qualquer outro uso
    // dela (um foreach, um Select) quer dizer que as linhas são lidas de fato, e o aviso não vale.
    private static (string Name, InvocationExpressionSyntax Call)? RemovalOnly(Hit hit, SyntaxNode body, SemanticModel model, CancellationToken ct)
    {
        (string, InvocationExpressionSyntax)? removal = null;
        var uses = body.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(id => id.SpanStart > hit.End.Span.End && id.Identifier.ValueText == hit.MaterializedInto)
            .ToList();
        foreach (var use in uses)
        {
            if (use.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } }
                && Symbol(call, model, ct) is { Name: "RemoveRange" } remove && IsEf(remove))
                removal ??= (remove.Name, call);
            else if (use.Parent is MemberAccessExpressionSyntax access && access.Expression == use && access.Name.Identifier.ValueText is "Count" or "Length")
                continue;
            else
                return null;
        }
        return removal;
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
        if (onContext && Operation([symbol.Name]) is { } op and not DbOperation.Read
            && Closed(EntityArgument(symbol, call, model, ct), scan.Method) is { } entity)
        {
            scan.Writes = true;
            scan.Hits.Add(new Hit(entity.Name, null, null, null, op, call, InLoop(call, body), false, null, null));
            return;
        }
        // _db.Entry(x).CurrentValues.SetValues(e): copia os valores sobre a entidade rastreada, é uma alteração.
        if (symbol.Name == "SetValues" && IsEf(symbol))
        {
            var entry = call.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Select(i => (Call: i, Symbol: Symbol(i, model, ct)))
                .FirstOrDefault(x => x.Symbol is { Name: "Entry" } && DerivesFrom(x.Symbol.ContainingType, "DbContext"));
            if (entry.Symbol is not null && Closed(EntityArgument(entry.Symbol, entry.Call, model, ct), scan.Method) is { } changed)
            {
                scan.Writes = true;
                scan.Hits.Add(new Hit(changed.Name, null, null, null, DbOperation.Update, call, InLoop(call, body), false, null, null));
            }
            return;
        }
        if (LikePattern(call, symbol, body, model, ct) is { } pattern)
        {
            scan.LeadingLikes.Add((call, pattern, LikeProperty(call, symbol)));
            return;
        }
        if (RawCommands.Contains(symbol.Name) && IsEf(symbol) && ProcedureOf(call) is { } procedure)
        {
            scan.Hits.Add(new Hit(null, null, procedure, symbol.Name, DbOperation.Execute, call, InLoop(call, body), false, null, RawSql(call, symbol.Name)));
            return;
        }
        if (!IsEf(symbol) && symbol.MethodKind == MethodKind.Ordinary)
        {
            scan.Calls.Add(call);
            if (InLoop(call, body)) scan.LoopCalls.Add(call);
            var (chain, end) = Chain(call);
            // A chamada cujo resultado o método devolve como está (return, => ou return await): um serviço fino.
            if (chain.All(c => c.Name == "ConfigureAwait") && end.Parent is ReturnStatementSyntax or ArrowExpressionClauseSyntax) scan.Returned.Add(call);
            if (FilterAfter(chain, 0, end, body, allowLocal: chain.All(c => c.Name == "ConfigureAwait")) is { } late)
                scan.FilteredCalls.Add((call, late.Filter, late.At));
        }
    }

    // O padrão de LIKE que o código monta e passa a EF.Functions.Like/ILike ou a Contains, quando começa por
    // curinga: a interpolação com % na frente, a concatenação com "%" na frente ou a variável que guarda um
    // desses. Devolve o texto do padrão.
    private static string? LikePattern(InvocationExpressionSyntax call, IMethodSymbol symbol, SyntaxNode body, SemanticModel model, CancellationToken ct)
    {
        ExpressionSyntax? argument = null;
        if (symbol.Name is "Like" or "ILike" && IsEf(symbol))
            argument = call.ArgumentList.Arguments.Count >= 2 ? call.ArgumentList.Arguments[1].Expression : null;
        else if (symbol.Name == "Contains" && symbol.ContainingType.SpecialType == SpecialType.System_String)
            argument = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        return argument is not null && StartsWithWildcard(argument, body, model, ct, 0) ? Collapse(argument) : null;
    }

    private static bool StartsWithWildcard(ExpressionSyntax expression, SyntaxNode body, SemanticModel model, CancellationToken ct, int depth)
    {
        if (depth > 3) return false;
        if (model.GetConstantValue(expression, ct) is { HasValue: true, Value: string text }) return text.StartsWith('%');
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return StartsWithWildcard(parenthesized.Expression, body, model, ct, depth);
            case InterpolatedStringExpressionSyntax interpolated:
                return interpolated.Contents.FirstOrDefault() is InterpolatedStringTextSyntax first && first.TextToken.ValueText.StartsWith('%');
            case BinaryExpressionSyntax binary when binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddExpression):
                return StartsWithWildcard(binary.Left, body, model, ct, depth);
            case IdentifierNameSyntax id:
                var declaration = body.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                    .FirstOrDefault(v => v.Identifier.ValueText == id.Identifier.ValueText && v.Initializer is not null && v.SpanStart < id.SpanStart);
                return declaration is not null && StartsWithWildcard(declaration.Initializer!.Value, body, model, ct, depth + 1);
            default:
                return false;
        }
    }

    // O membro da entidade que o LIKE compara (t.Titulo em ILike(t.Titulo, ...) ou em t.Titulo.Contains(...)),
    // sem o ToLower, o Trim e o "!" em volta. Nulo quando não é um membro simples.
    private static string? LikeProperty(InvocationExpressionSyntax call, IMethodSymbol symbol)
    {
        ExpressionSyntax? target = symbol.Name is "Like" or "ILike" && IsEf(symbol)
            ? call.ArgumentList.Arguments.FirstOrDefault()?.Expression
            : (call.Expression as MemberAccessExpressionSyntax)?.Expression;
        while (target is not null)
        {
            switch (target)
            {
                case ParenthesizedExpressionSyntax p: target = p.Expression; break;
                case PostfixUnaryExpressionSyntax { Operand: var operand }: target = operand; break;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax m }: target = m.Expression; break;
                case MemberAccessExpressionSyntax m: return m.Name.Identifier.ValueText;
                default: return null;
            }
        }
        return null;
    }

    // O índice de trigrama (GIN ou GiST com pg_trgm) da coluna que o LIKE compara atende o curinga no começo.
    // A coluna sai do nome do membro sem maiúsculas e sublinhados (Titulo, titulo, titulo_busca não se confundem
    // com ArquivadaEm, arquivada_em). Sem coluna conhecida, o achado fica: não dá para afirmar o índice.
    private static bool HasTrigramIndex(DbTable? table, string? property)
    {
        if (table is null || property is null) return false;
        static string Key(string name) => DbNames.Unquote(name).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        var column = table.Columns.FirstOrDefault(c => Key(c.Name) == Key(property));
        return column is not null && table.Indexes.Any(i => i.IsTrigram(column.Name));
    }

    // A consulta do banco que contém o LIKE; sem uma que o contenha, a única tabela lida pelo método.
    private static Hit? LikeTable(SyntaxNode like, MethodScan scan)
    {
        var inside = scan.Hits.FirstOrDefault(h => h.Operation == DbOperation.Read && h.End.Span.Contains(like.Span));
        if (inside is not null) return inside;
        var reads = scan.Hits.Where(h => h.Operation == DbOperation.Read && h.Entity is not null).DistinctBy(h => h.Entity).ToList();
        return reads.Count == 1 ? reads[0] : null;
    }

    private static string Collapse(SyntaxNode node) => Regex.Replace(node.ToString(), @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Trim();

    private static Hit? FromDbSet(ExpressionSyntax expr, string entity, SyntaxNode body, SemanticModel model, CancellationToken ct)
    {
        // O nome da propriedade do DbContext (Pedidos), que serve de tabela quando o esquema não diz; um
        // campo do repositório genérico (_set) ou um Set<T>() não é nome de tabela.
        var setSymbol = model.GetSymbolInfo(expr, ct).Symbol;
        var setName = setSymbol is IPropertySymbol or IFieldSymbol && DerivesFrom(setSymbol.ContainingType, "DbContext") ? setSymbol.Name : null;
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
        var sql = raw.Call is null ? null : RawSql(raw.Call, raw.Name);
        var materializer = chain.FirstOrDefault(c => Materializers.Contains(c.Name)).Name;
        var materializedLast = materializer is not null && Materializers.Contains(chain[^1].Name);
        return new Hit(entity, setName, procedure, rawMethod, op, expr, InLoop(expr, body), tracked, LateFilter(chain, end, body), sql)
        {
            End = end,
            IsFind = op == DbOperation.Read && names.Any(n => n is "Find" or "FindAsync"),
            Materializer = materializer,
            MaterializedInto = materializedLast && end.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable } ? variable.Identifier.ValueText : null,
            ReturnsMaterializedBy = op == DbOperation.Read && materializedLast && end.Parent is ReturnStatementSyntax or ArrowExpressionClauseSyntax ? materializer : null
        };
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
        return FilterAfter(chain, at + 1, end, body, allowLocal: at == chain.Count - 1) is { } late ? (chain[at].Name, late.Filter, late.At) : null;
    }

    // O primeiro filtro aplicado ao resultado, a partir de chain[from]: na própria cadeia, ou no uso da variável
    // que o recebe. Só chamada conta (Where(...), Count()): ".Count" sem parênteses é o tamanho da lista, que já
    // está na memória, e não um filtro.
    private static (string Filter, SyntaxNode At)? FilterAfter(List<(string Name, InvocationExpressionSyntax Call)> chain, int from, ExpressionSyntax end, SyntaxNode body, bool allowLocal)
    {
        var filter = chain.Skip(from).FirstOrDefault(c => Filters.Contains(c.Name));
        if (filter.Call is not null) return (filter.Name, filter.Call);
        if (!allowLocal || end.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable }) return null;

        var local = variable.Identifier.ValueText;
        var use = body.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(m => m.SpanStart > variable.Span.End && m.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == local
                && Filters.Contains(m.Name.Identifier.ValueText) && m.Parent is InvocationExpressionSyntax call && call.Expression == m)
            .OrderBy(m => m.SpanStart)
            .FirstOrDefault();
        return use is null ? null : (use.Name.Identifier.ValueText, use);
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

    // O texto do SQL cru, quando é literal no código (Fase 40): "{0}" do FromSqlRaw e os buracos da string
    // interpolada viram @p0, @p1, como o EF manda ao banco, para o leitor de T-SQL entender.
    private static string? RawSql(InvocationExpressionSyntax call, string method)
    {
        var argument = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        switch (argument)
        {
            case LiteralExpressionSyntax literal when literal.Token.Value is string text:
                return method.EndsWith("Raw", StringComparison.Ordinal) ? Placeholder.Replace(text, "@p$1") : text;
            case InterpolatedStringExpressionSyntax interpolated:
                var hole = 0;
                return string.Concat(interpolated.Contents.Select(c => c is InterpolatedStringTextSyntax t ? t.TextToken.ValueText : $"@p{hole++}"));
            default:
                return null;
        }
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

    private static ITypeSymbol? DbSetEntity(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "DbSet", TypeArguments.Length: 1 } set ? set.TypeArguments[0] : null;

    // O tipo da entidade já fechado: o T de um repositório genérico vira o tipo da chamada (Tarefa). Sem T
    // fechado (o método foi alcançado pelo tipo aberto), devolve nulo.
    private static ITypeSymbol? Closed(ITypeSymbol? type, IMethodSymbol method)
    {
        if (type is null or IErrorTypeSymbol) return null;
        if (type is not ITypeParameterSymbol parameter) return type;
        var owner = method.ContainingType;
        if (owner is null || !owner.IsGenericType) return null;
        for (var i = 0; i < owner.OriginalDefinition.TypeParameters.Length; i++)
            if (SymbolEqualityComparer.Default.Equals(owner.OriginalDefinition.TypeParameters[i], parameter))
                return owner.TypeArguments[i] is ITypeParameterSymbol ? null : owner.TypeArguments[i];
        return null;
    }

    // "_db.Pedidos" é o DbSet; o "_db.Set<Pedido>" dentro de "_db.Set<Pedido>()" não conta duas vezes.
    private static bool IsInsideDbSetExpression(ExpressionSyntax expr, SemanticModel model, CancellationToken ct) =>
        expr.Parent is InvocationExpressionSyntax inv && inv.Expression == expr && DbSetEntity(model.GetTypeInfo(inv, ct).Type) is not null;

    private static ITypeSymbol? EntityArgument(IMethodSymbol symbol, InvocationExpressionSyntax call, SemanticModel model, CancellationToken ct)
    {
        if (symbol.IsGenericMethod && symbol.TypeArguments.Length == 1 && symbol.TypeArguments[0] is { SpecialType: SpecialType.None } t
            && t.Name != "Object" && t is INamedTypeSymbol or ITypeParameterSymbol)
            return t;
        var argument = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        var type = argument is null ? null : model.GetTypeInfo(argument, ct).Type;
        return type switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol { TypeArguments.Length: 1 } generic when symbol.Name.EndsWith("Range", StringComparison.Ordinal) => generic.TypeArguments[0],
            INamedTypeSymbol { SpecialType: SpecialType.None } named when named.Name != "Object" => named,
            ITypeParameterSymbol parameter => parameter,
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
        (string Materializer, string Filter, SyntaxNode At)? LateFilter,
        string? Sql)
    {
        // O fim da cadeia que sai do DbSet (depois de await e parênteses): onde o resultado é usado.
        public SyntaxNode End { get; init; } = At;
        // Leitura de uma entidade por chave (Find, FindAsync).
        public bool IsFind { get; init; }
        public string? Materializer { get; init; }
        // A variável que recebe a lista materializada (var l = await ...ToListAsync()).
        public string? MaterializedInto { get; init; }
        // O materializador, quando a consulta devolve a lista como resultado do método.
        public string? ReturnsMaterializedBy { get; init; }
    }

    private sealed class MethodScan(IMethodSymbol method)
    {
        public IMethodSymbol Method { get; } = method;
        public List<Hit> Hits { get; } = [];
        // Toda chamada a outro método do código (não do EF), para seguir a cadeia até quem consulta.
        public List<InvocationExpressionSyntax> Calls { get; } = [];
        public List<InvocationExpressionSyntax> LoopCalls { get; } = [];
        // As chamadas cujo resultado o método devolve sem tocar (serviço fino).
        public List<InvocationExpressionSyntax> Returned { get; } = [];
        public List<(InvocationExpressionSyntax Call, string Filter, SyntaxNode At)> FilteredCalls { get; } = [];
        public List<(Hit Hit, (string Name, InvocationExpressionSyntax Call) Removal)> LoadToDelete { get; } = [];
        public List<(InvocationExpressionSyntax Call, string Pattern, string? Property)> LeadingLikes { get; } = [];
        public bool Writes { get; set; }
    }
}
