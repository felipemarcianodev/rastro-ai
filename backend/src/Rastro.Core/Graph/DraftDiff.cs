namespace Rastro.Core.Graph;

public enum DraftChangeType
{
    AddedStep = 0,
    ModifiedStep = 1,
    RemovedStep = 2,
    MovedStep = 3,
    AddedLink = 4,
    RemovedLink = 5
}

// Uma mudança do rascunho, já resolvida para quem vai escrever o código: o passo, de onde ele
// é chamado e o que vem depois. "Before"/"After" listam rótulos de nós (para o prompt ler bem).
public record DraftChange(
    DraftChangeType Type,
    FlowNode Node,
    IReadOnlyList<FlowNode> CalledFrom,
    IReadOnlyList<FlowNode> Calls,
    FlowNode? PreviousCaller = null,
    FlowNode? Other = null,
    string? Branch = null);

// Diferença entre o grafo extraído e o rascunho (Fase 8). Determinística: o mesmo rascunho gera
// sempre a mesma lista, na ordem do fluxo.
public static class DraftDiff
{
    public static IReadOnlyList<DraftChange> Compute(FlowGraph original, FlowGraph draft)
    {
        var nodes = draft.Nodes.ToDictionary(n => n.Id);
        var order = FlowOrder(draft);
        int Rank(string id) => order.TryGetValue(id, out var rank) ? rank : int.MaxValue;

        IReadOnlyList<FlowNode> Callers(string id) => draft.Edges
            .Where(e => e.To == id && e.Change != ChangeKind.Removed).Select(e => nodes[e.From]).ToList();
        IReadOnlyList<FlowNode> Callees(string id) => draft.Edges
            .Where(e => e.From == id && e.Change != ChangeKind.Removed).OrderBy(e => e.Order).Select(e => nodes[e.To]).ToList();

        var changes = new List<DraftChange>();
        var explainedEdges = new HashSet<FlowEdge>();

        foreach (var node in draft.Nodes.Where(n => n.Change is not null).OrderBy(n => Rank(n.Id)))
        {
            switch (node.Change)
            {
                case ChangeKind.Added:
                    changes.Add(new DraftChange(DraftChangeType.AddedStep, node, Callers(node.Id), Callees(node.Id)));
                    foreach (var edge in draft.Edges.Where(e => e.Change == ChangeKind.Added && (e.From == node.Id || e.To == node.Id)))
                        explainedEdges.Add(edge);
                    break;
                case ChangeKind.Modified:
                    changes.Add(new DraftChange(DraftChangeType.ModifiedStep, node, Callers(node.Id), Callees(node.Id)));
                    break;
                case ChangeKind.Removed:
                    var formerCallers = draft.Edges.Where(e => e.To == node.Id).Select(e => nodes[e.From]).ToList();
                    changes.Add(new DraftChange(DraftChangeType.RemovedStep, node, formerCallers, []));
                    foreach (var edge in draft.Edges.Where(e => e.Change == ChangeKind.Removed && (e.From == node.Id || e.To == node.Id)))
                        explainedEdges.Add(edge);
                    break;
            }
        }

        // Passo movido: ligação do código que sai e ligação nova que chega ao mesmo passo.
        var removedLinks = draft.Edges.Where(e => e.Change == ChangeKind.Removed && !explainedEdges.Contains(e)).ToList();
        var addedLinks = draft.Edges.Where(e => e.Change == ChangeKind.Added && !explainedEdges.Contains(e)).ToList();
        foreach (var removed in removedLinks.ToList())
        {
            var moved = addedLinks.FirstOrDefault(a => a.To == removed.To && a.From != removed.From);
            if (moved is null) continue;
            changes.Add(new DraftChange(DraftChangeType.MovedStep, nodes[removed.To], [nodes[moved.From]], Callees(removed.To),
                PreviousCaller: nodes[removed.From], Branch: moved.Label));
            removedLinks.Remove(removed);
            addedLinks.Remove(moved);
        }

        changes.AddRange(addedLinks.Select(e =>
            new DraftChange(DraftChangeType.AddedLink, nodes[e.From], [], [], Other: nodes[e.To], Branch: e.Label)));
        changes.AddRange(removedLinks.Select(e =>
            new DraftChange(DraftChangeType.RemovedLink, nodes[e.From], [], [], Other: nodes[e.To], Branch: e.Label)));

        return changes;
    }

    // Posição de cada nó na ordem do fluxo (profundidade primeiro, na ordem de execução).
    public static IReadOnlyDictionary<string, int> FlowOrder(FlowGraph graph)
    {
        var outgoing = graph.Edges.Where(e => e.Change != ChangeKind.Removed).ToLookup(e => e.From);
        var order = new Dictionary<string, int>();
        var stack = new Stack<string>([graph.EntryNodeId]);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (order.ContainsKey(id)) continue;
            order[id] = order.Count;
            foreach (var edge in outgoing[id].OrderByDescending(e => e.Order))
                if (!order.ContainsKey(edge.To)) stack.Push(edge.To);
        }
        foreach (var node in graph.Nodes.Where(n => !order.ContainsKey(n.Id)))
            order[node.Id] = order.Count;
        return order;
    }
}
