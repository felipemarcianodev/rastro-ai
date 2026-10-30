using Microsoft.Extensions.Logging;
using Rastro.Core.Graph;
using Rastro.Core.Http;
using Rastro.Extractor;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Infrastructure.Analysis;

// Trecho do levantamento além do primeiro: o repositório de destino de uma chamada HTTP, a solution
// aberta nele, o método de entrada e o commit em que foi lido (vira um SurveySegment).
public record SegmentInfo(string Repository, string SolutionPath, string EntryPoint, string? CommitHash, string? Branch);

public record CrossAnalysis(FlowGraph Graph, IReadOnlyList<SegmentInfo> Segments);

// Costura entre sistemas (Fase 25). Percorre o primeiro trecho, acha pelo índice de rotas quem atende cada
// chamada HTTP e continua o percurso dentro do outro repositório, como um trecho novo, com os ids prefixados
// pelo sistema. Abre UMA solution por vez: solta cada uma antes de abrir a próxima.
public class CrossSystemAnalyzer(
    ISolutionSource solutions,
    RepositoryCatalog catalog,
    RouteIndexStore routeIndexes,
    IGitInfo git,
    ILogger<CrossSystemAnalyzer> logger)
{
    // Teto de trechos por levantamento: segura a explosão de um grafo de serviços muito ligados.
    public const int MaxSegments = 30;

    private sealed record Pending(string NodeId, HttpCall Call, int Depth);

    private sealed record Resolution(Pending Call, string Repository, RouteEntry Entry);

    public async Task<CrossAnalysis> AnalyzeAsync(
        ResolvedTarget first, string entryPoint, int maxDepth, IReadOnlyList<string> crossRepositories, CancellationToken ct)
    {
        if (crossRepositories.Count == 0)
        {
            var plain = await OpenAsync(first, ct);
            try
            {
                var graph = await FlowExtractor.ExtractAsync(plain.Solution, entryPoint,
                    new ExtractionOptions { MaxDepth = maxDepth, BaseDirectory = plain.BaseDirectory }, ct);
                return new CrossAnalysis(graph, []);
            }
            catch (FlowEntryNotFoundException ex)
            {
                throw new AnalysisException(ex.Message, ex);
            }
        }

        var indexes = new Dictionary<string, IReadOnlyList<RouteEntry>>(StringComparer.Ordinal);
        foreach (var repository in crossRepositories.Distinct(StringComparer.OrdinalIgnoreCase))
            indexes[repository] = await routeIndexes.GetAsync(repository, ct);

        // Primeiro trecho: sem prefixo, o grafo sai idêntico ao de um levantamento sem costura.
        ExtractionResult firstResult;
        var handle = await OpenAsync(first, ct);
        try
        {
            firstResult = await FlowExtractor.ExtractWithCallsAsync(handle.Solution, entryPoint,
                new ExtractionOptions { MaxDepth = maxDepth, BaseDirectory = handle.BaseDirectory, HttpCalls = true }, ct);
        }
        catch (FlowEntryNotFoundException ex)
        {
            throw new AnalysisException(ex.Message, ex);
        }
        finally
        {
            solutions.Release(first);
        }

        var nodes = firstResult.Graph.Nodes.ToList();
        var edges = firstResult.Graph.Edges.ToList();
        var findings = (firstResult.Graph.Findings ?? []).ToList();
        var segments = new List<SegmentInfo>();
        var handled = new HashSet<string>(StringComparer.Ordinal);
        // Método (sistema + Namespace.Tipo.Metodo) -> id do nó no grafo. Destino que já está no grafo, seja por
        // ciclo, seja por uma segunda chamada ao mesmo endpoint, vira só a aresta.
        var methodIds = new Dictionary<string, string>(StringComparer.Ordinal);
        Register(first.RepositoryName, firstResult.Graph.Nodes);
        var heads = new Dictionary<string, GitHead?>(StringComparer.OrdinalIgnoreCase);
        var pending = firstResult.HttpCalls.Select(c => new Pending(c.NodeId, c.Call, c.Depth)).ToList();
        logger.LogInformation("Trecho inicial extraído: {NodeCount} nós, {CallCount} chamada(s) HTTP.", nodes.Count, pending.Count);

        while (pending.Count > 0)
        {
            var toResolve = new List<Resolution>();
            foreach (var call in pending)
            {
                if (!handled.Add(call.NodeId)) continue;
                var matches = RouteMatcher.Match(call.Call, indexes);
                if (matches.Count == 0) continue; // O nó já saiu do extrator como NoMatchingEndpoint ou RouteNotResolved.
                if (matches.Count > 1)
                {
                    var candidates = string.Join("; ", matches.Select(m => $"{m.Repository}: {Short(m.Entry.Target)}"));
                    Update(call.NodeId, n => n with { Reason = UnresolvedReason.MultipleEndpoints, Detail = $"A rota casa com mais de um destino: {candidates}." });
                    continue;
                }
                toResolve.Add(new Resolution(call, matches[0].Repository, matches[0].Entry));
            }

            pending = [];
            foreach (var group in toResolve.GroupBy(r => r.Repository, StringComparer.Ordinal))
            {
                var repository = group.Key;
                var target = ResolveTarget(repository);
                if (target is null)
                {
                    logger.LogWarning("Repositório {Repository} sem solution utilizável para a costura.", repository);
                    foreach (var r in group)
                        Update(r.Call.NodeId, n => n with { Reason = UnresolvedReason.NoMatchingEndpoint, Detail = $"O repositório {repository} atende a rota, mas não tem solution para abrir." });
                    continue;
                }

                SolutionHandle? open = null;
                var openFailed = false;
                try
                {
                    foreach (var r in group)
                    {
                        var remaining = maxDepth - (r.Call.Depth + 1);
                        if (remaining < 1)
                        {
                            Update(r.Call.NodeId, n => n with { Reason = UnresolvedReason.DepthLimit, Detail = $"A chamada segue para {repository}: {Short(r.Entry.Target)}, mas o limite de profundidade foi atingido." });
                            continue;
                        }

                        if (methodIds.TryGetValue(MethodKey(repository, r.Entry.Target), out var knownEntry))
                        {
                            Link(r.Call.NodeId, knownEntry);
                            continue;
                        }

                        if (segments.Count >= MaxSegments)
                        {
                            logger.LogWarning("Limite de {MaxSegments} trechos atingido; a chamada para {Repository} ficou sem seguir.", MaxSegments, repository);
                            Update(r.Call.NodeId, n => n with { Reason = UnresolvedReason.DepthLimit, Detail = $"Limite de {MaxSegments} trechos por levantamento atingido antes de seguir para {repository}: {Short(r.Entry.Target)}." });
                            continue;
                        }

                        if (open is null && !openFailed)
                        {
                            try
                            {
                                open = await OpenAsync(target, ct);
                            }
                            catch (AnalysisException ex)
                            {
                                openFailed = true;
                                logger.LogWarning(ex, "Solution do repositório {Repository} não abriu; a costura não segue para ele.", repository);
                            }
                        }

                        if (open is null)
                        {
                            Update(r.Call.NodeId, n => n with { Reason = UnresolvedReason.NoMatchingEndpoint, Detail = $"A solution de {repository} não abriu, então a chamada não foi seguida." });
                            continue;
                        }

                        ExtractionResult segment;
                        try
                        {
                            segment = await FlowExtractor.ExtractWithCallsAsync(open.Solution, r.Entry.Target,
                                new ExtractionOptions { MaxDepth = remaining, BaseDirectory = open.BaseDirectory, HttpCalls = true }, ct);
                        }
                        catch (FlowEntryNotFoundException ex)
                        {
                            logger.LogWarning(ex, "Destino da rota não encontrado na solution de {Repository}.", repository);
                            Update(r.Call.NodeId, n => n with { Reason = UnresolvedReason.NoMatchingEndpoint, Detail = $"O índice de rotas aponta para {Short(r.Entry.Target)}, mas a solution de {repository} não achou o método." });
                            continue;
                        }

                        var inSystem = SystemIds.InSystem(segment.Graph, repository);
                        MergeSegment(inSystem);
                        Register(repository, inSystem.Nodes);
                        methodIds.TryAdd(MethodKey(repository, r.Entry.Target), inSystem.EntryNodeId);
                        Link(r.Call.NodeId, inSystem.EntryNodeId);

                        if (!heads.TryGetValue(repository, out var head))
                            heads[repository] = head = await git.HeadAsync(target.RepositoryRoot, ct);
                        segments.Add(new SegmentInfo(repository, target.SolutionRelativePath, r.Entry.Target, head?.Commit, head?.Branch));

                        var added = 0;
                        foreach (var site in segment.HttpCalls)
                        {
                            var id = SystemIds.Prefix(repository, site.NodeId);
                            if (handled.Contains(id) || nodes.All(n => n.Id != id)) continue;
                            pending.Add(new Pending(id, site.Call, r.Call.Depth + 1 + site.Depth));
                            added++;
                        }
                        logger.LogInformation("Trecho {Repository} extraído: {NodeCount} nós, {CallCount} chamada(s) HTTP a seguir.",
                            repository, inSystem.Nodes.Count, added);
                    }
                }
                finally
                {
                    solutions.Release(target);
                }
            }
        }

        var result = firstResult.Graph with
        {
            Nodes = nodes,
            Edges = edges,
            Findings = findings.Count == 0 && firstResult.Graph.Findings is null ? null : findings
        };
        return new CrossAnalysis(result, segments);

        // Nós e arestas do trecho novo entram no grafo; o que já existe (métodos comuns a dois destinos) não se repete.
        // Chamada HTTP já tratada em outro trecho não volta: a aresta que a substituiu já está no grafo.
        void MergeSegment(FlowGraph segment)
        {
            var known = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var node in segment.Nodes)
            {
                if (known.Contains(node.Id) || handled.Contains(node.Id)) continue;
                nodes.Add(node.Kind == NodeKind.Entry ? node with { Kind = NodeKind.Method } : node);
            }

            var keys = edges.Select(e => (e.From, e.To, e.Label)).ToHashSet();
            foreach (var edge in segment.Edges)
                if (!handled.Contains(edge.To) && keys.Add((edge.From, edge.To, edge.Label)))
                    edges.Add(edge);

            var findingKeys = findings.Select(f => (f.NodeId, f.Message)).ToHashSet();
            foreach (var finding in segment.Findings ?? [])
                if (findingKeys.Add((finding.NodeId, finding.Message)))
                    findings.Add(finding);
        }

        // Troca o nó "h:" por uma aresta até o método de destino: a aresta que chegava nele passa a ir direto
        // ao destino, com a mesma ordem, rótulo, posição e chamada HTTP. Se o destino já estava no grafo
        // (ciclo ou segunda chamada ao mesmo endpoint), só a aresta muda.
        void Link(string callNodeId, string entryId)
        {
            for (var i = 0; i < edges.Count; i++)
                if (edges[i].To == callNodeId)
                    edges[i] = edges[i] with { To = entryId };
            nodes.RemoveAll(n => n.Id == callNodeId);
        }

        void Register(string system, IEnumerable<FlowNode> added)
        {
            foreach (var node in added.Where(n => n.Kind is NodeKind.Entry or NodeKind.Method && n.Symbol is not null))
            {
                var symbol = node.Symbol!;
                var open = symbol.IndexOf('(');
                methodIds.TryAdd(MethodKey(system, open < 0 ? symbol : symbol[..open]), node.Id);
            }
        }

        void Update(string nodeId, Func<FlowNode, FlowNode> change)
        {
            var index = nodes.FindIndex(n => n.Id == nodeId);
            if (index >= 0) nodes[index] = change(nodes[index]);
        }
    }

    // Primeira solution do repositório, da mais rasa para a mais funda (a ordem do catálogo).
    private ResolvedTarget? ResolveTarget(string repository) =>
        catalog.Solutions(repository)?.Select(s => catalog.Resolve(repository, s).Target).FirstOrDefault(t => t is not null);

    private Task<SolutionHandle> OpenAsync(ResolvedTarget target, CancellationToken ct) =>
        solutions.OpenAsync(target, fresh: true, ct);

    private static string MethodKey(string system, string target) => $"{system.ToLowerInvariant()}|{target}";

    // "Namespace.Tipo.Metodo" -> "Tipo.Metodo".
    private static string Short(string target)
    {
        var parts = target.Split('.');
        return parts.Length <= 2 ? target : string.Join('.', parts[^2..]);
    }
}
