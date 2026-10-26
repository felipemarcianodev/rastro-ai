namespace Rastro.Core.Graph;

// Consultas de impacto (ideia do dono: o Rastro como "memória de fluxo" de agentes de código).
public static class GraphQueries
{
    // Caminhos da entrada até o nó (cada caminho é uma lista de nós). Limitado para grafos grandes.
    public static IReadOnlyList<IReadOnlyList<FlowNode>> PathsTo(FlowGraph graph, string nodeId, int maxPaths = 10)
    {
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        var incoming = graph.Edges.Where(e => e.Change != ChangeKind.Removed).ToLookup(e => e.To);
        var paths = new List<IReadOnlyList<FlowNode>>();

        void Walk(string id, List<string> suffix, HashSet<string> seen)
        {
            if (paths.Count >= maxPaths) return;
            suffix.Insert(0, id);
            if (id == graph.EntryNodeId)
                paths.Add(suffix.Select(s => nodes[s]).ToList());
            else
                foreach (var edge in incoming[id])
                    if (seen.Add(edge.From))
                    {
                        Walk(edge.From, suffix, seen);
                        seen.Remove(edge.From);
                    }
            suffix.RemoveAt(0);
        }

        if (nodes.ContainsKey(nodeId)) Walk(nodeId, [], [nodeId]);
        return paths;
    }

    // Tudo o que roda depois do nó (o que pode quebrar se ele mudar).
    public static IReadOnlyList<FlowNode> Downstream(FlowGraph graph, string nodeId)
    {
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        var outgoing = graph.Edges.Where(e => e.Change != ChangeKind.Removed).ToLookup(e => e.From);
        var seen = new HashSet<string> { nodeId };
        var queue = new Queue<string>([nodeId]);
        var result = new List<FlowNode>();
        while (queue.Count > 0)
            foreach (var edge in outgoing[queue.Dequeue()])
                if (seen.Add(edge.To))
                {
                    result.Add(nodes[edge.To]);
                    queue.Enqueue(edge.To);
                }
        return result;
    }
}
