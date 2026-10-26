namespace Rastro.Core.Graph;

// Confere um rascunho de edição contra o grafo extraído. O rascunho só pode MARCAR o que é do
// código (removido ou modificado, com intenção) e ACRESCENTAR o que é novo; nunca apagar ou trocar
// o que veio do compilador, porque o prompt da Fase 8 nasce exatamente dessa diferença.
public static class DraftValidator
{
    public const string AddedPrefix = "new:";
    public const int MaxAddedNodes = 200;
    public const int MaxLabelLength = 200;
    public const int MaxIntentLength = 2000;

    public static IReadOnlyList<string> Validate(FlowGraph original, FlowGraph draft)
    {
        var errors = new List<string>();
        if (draft.EntryNodeId != original.EntryNodeId)
            errors.Add("O rascunho precisa manter a mesma entrada do levantamento.");

        var duplicated = draft.Nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicated.Count > 0) errors.Add($"Nós repetidos no rascunho: {string.Join(", ", duplicated.Take(5))}.");

        var draftNodes = draft.Nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var node in original.Nodes)
        {
            if (!draftNodes.TryGetValue(node.Id, out var edited))
            {
                errors.Add($"O passo \"{node.Label}\" do código sumiu do rascunho. Para tirá-lo, marque como removido.");
                continue;
            }
            if (edited.Kind != node.Kind || edited.Label != node.Label || edited.Location != node.Location)
                errors.Add($"O passo \"{node.Label}\" do código foi alterado. Só dá para marcá-lo como removido ou modificado.");
            if (edited.Change == ChangeKind.Added)
                errors.Add($"O passo \"{node.Label}\" já existe no código; não pode ser marcado como novo.");
            if (edited.Change == ChangeKind.Modified && string.IsNullOrWhiteSpace(edited.Intent))
                errors.Add($"O passo \"{node.Label}\" foi marcado como modificado sem dizer o que muda.");
        }

        var added = draft.Nodes.Where(n => !original.Nodes.Any(o => o.Id == n.Id)).ToList();
        if (added.Count > MaxAddedNodes) errors.Add($"No máximo {MaxAddedNodes} passos novos por rascunho.");
        foreach (var node in added)
        {
            if (!node.Id.StartsWith(AddedPrefix, StringComparison.Ordinal) || node.Change != ChangeKind.Added)
                errors.Add($"O passo novo \"{node.Label}\" precisa de id iniciado por \"{AddedPrefix}\" e marca de novo.");
            if (string.IsNullOrWhiteSpace(node.Label) || node.Label.Length > MaxLabelLength)
                errors.Add($"Passo novo sem nome (ou com mais de {MaxLabelLength} caracteres).");
            if (string.IsNullOrWhiteSpace(node.Intent))
                errors.Add($"O passo novo \"{node.Label}\" precisa dizer o que deve fazer.");
            if (node.Kind is not (NodeKind.Method or NodeKind.Decision))
                errors.Add($"O passo novo \"{node.Label}\" só pode ser um método ou uma decisão.");
        }

        foreach (var node in draft.Nodes.Where(n => n.Intent is { Length: > MaxIntentLength }))
            errors.Add($"A intenção de \"{node.Label}\" passa de {MaxIntentLength} caracteres.");

        foreach (var edge in original.Edges)
        {
            var kept = draft.Edges.FirstOrDefault(e => e.From == edge.From && e.To == edge.To && e.Label == edge.Label);
            if (kept is null)
                errors.Add($"Uma ligação do código ({Label(original, edge.From)} → {Label(original, edge.To)}) sumiu. Para tirá-la, marque como removida.");
            else if (kept.Change == ChangeKind.Added)
                errors.Add($"A ligação {Label(original, edge.From)} → {Label(original, edge.To)} já existe no código; não pode ser marcada como nova.");
        }

        foreach (var edge in draft.Edges.Where(e => e.Change == ChangeKind.Added))
            if (!draftNodes.ContainsKey(edge.From) || !draftNodes.ContainsKey(edge.To))
                errors.Add("Uma ligação nova aponta para um passo que não está no rascunho.");

        return errors;
    }

    public static bool HasChanges(FlowGraph draft) =>
        draft.Nodes.Any(n => n.Change is not null) || draft.Edges.Any(e => e.Change is not null);

    private static string Label(FlowGraph graph, string id) => graph.Nodes.FirstOrDefault(n => n.Id == id)?.Label ?? id;
}
