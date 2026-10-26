namespace Rastro.Core.Graph;

// Mudanças no grafo depois da extração. Funções puras: recebem um grafo e devolvem outro.
// O núcleo não sabe de onde veio a proposta (agente, provedor, usuário); só registra a origem.
public static class GraphEditor
{
    // Liga um nó não resolvido ao método inferido e acrescenta o rastro a partir dele
    // (o subgrafo extraído com o método como entrada). Nós que já existem não se repetem.
    // Um buraco pode ter vários destinos (despacho por tipo de evento, por exemplo): "when" diz
    // quando este roda e vira o rótulo da aresta.
    public static FlowGraph WithInferredBranch(FlowGraph graph, string fromNodeId, FlowGraph branch, ProposalOrigin origin, string? when = null)
    {
        var nodes = graph.Nodes.ToList();
        var known = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var node in branch.Nodes)
        {
            if (known.Contains(node.Id)) continue;
            nodes.Add(node.Kind == NodeKind.Entry ? node with { Kind = NodeKind.Method } : node);
            known.Add(node.Id);
        }

        var edges = graph.Edges.ToList();
        var edgeKeys = edges.Select(e => (e.From, e.To, e.Label)).ToHashSet();
        foreach (var edge in branch.Edges)
            if (edgeKeys.Add((edge.From, edge.To, edge.Label)))
                edges.Add(edge);

        if (edges.All(e => !(e.From == fromNodeId && e.To == branch.EntryNodeId)))
        {
            var order = edges.Where(e => e.From == fromNodeId).Select(e => e.Order).DefaultIfEmpty(0).Max() + 1;
            edges.Add(new FlowEdge(fromNodeId, branch.EntryNodeId, order, EdgeConfidence.Inferred, string.IsNullOrWhiteSpace(when) ? null : when.Trim(), origin));
        }

        return graph with { Nodes = nodes, Edges = edges };
    }

    // Decisão do usuário sobre uma aresta proposta: confirmar vira Confirmed; descartar remove
    // a aresta e tudo o que só era alcançável por ela. Arestas extraídas não mudam.
    public static FlowGraph WithEdgeDecision(FlowGraph graph, string from, string to, bool confirm)
    {
        var target = graph.Edges.FirstOrDefault(e => e.From == from && e.To == to && e.Confidence != EdgeConfidence.Extracted)
            ?? throw new InvalidOperationException("Não há aresta proposta entre esses nós.");

        if (confirm)
        {
            return graph with
            {
                Edges = graph.Edges.Select(e => ReferenceEquals(e, target) ? e with { Confidence = EdgeConfidence.Confirmed } : e).ToList()
            };
        }

        var edges = graph.Edges.Where(e => !ReferenceEquals(e, target)).ToList();
        return Prune(graph with { Edges = edges });
    }

    public static FlowGraph WithRule(FlowGraph graph, string nodeId, NodeRule rule)
    {
        if (graph.Nodes.All(n => n.Id != nodeId))
            throw new InvalidOperationException("Nó não encontrado no grafo.");

        return graph with { Nodes = graph.Nodes.Select(n => n.Id == nodeId ? n with { Rule = rule } : n).ToList() };
    }

    // Remove nós e arestas que não são mais alcançáveis a partir da entrada.
    public static FlowGraph Prune(FlowGraph graph)
    {
        var outgoing = graph.Edges.ToLookup(e => e.From);
        var reachable = new HashSet<string>(StringComparer.Ordinal) { graph.EntryNodeId };
        var queue = new Queue<string>([graph.EntryNodeId]);
        while (queue.Count > 0)
            foreach (var edge in outgoing[queue.Dequeue()])
                if (reachable.Add(edge.To)) queue.Enqueue(edge.To);

        return graph with
        {
            Nodes = graph.Nodes.Where(n => reachable.Contains(n.Id)).ToList(),
            Edges = graph.Edges.Where(e => reachable.Contains(e.From) && reachable.Contains(e.To)).ToList()
        };
    }
}
