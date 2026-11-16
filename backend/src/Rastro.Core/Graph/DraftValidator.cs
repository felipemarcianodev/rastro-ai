using System.Globalization;
using Rastro.Core.Failures;

namespace Rastro.Core.Graph;

// Um erro do rascunho: o código e os parâmetros (a tela traduz) com o texto em português ao lado.
public record DraftIssue(DraftError Code, IReadOnlyList<string> Args)
{
    public DraftIssue(DraftError code, params string[] args) : this(code, (IReadOnlyList<string>)args) { }

    public string Text => FailureText.Of(Code, Args);
}

// Confere um rascunho de edição contra o grafo extraído. O rascunho só pode MARCAR o que é do
// código (removido ou modificado, com intenção) e ACRESCENTAR o que é novo; nunca apagar ou trocar
// o que veio do compilador, porque o prompt da Fase 8 nasce exatamente dessa diferença.
public static class DraftValidator
{
    public const string AddedPrefix = "new:";
    public const int MaxAddedNodes = 200;
    public const int MaxLabelLength = 200;
    public const int MaxIntentLength = 2000;

    // Texto em português de cada erro (testes, log). A tela usa ValidateCoded e traduz pelo código.
    public static IReadOnlyList<string> Validate(FlowGraph original, FlowGraph draft) =>
        ValidateCoded(original, draft).Select(e => e.Text).ToList();

    public static IReadOnlyList<DraftIssue> ValidateCoded(FlowGraph original, FlowGraph draft)
    {
        var errors = new List<DraftIssue>();
        if (draft.EntryNodeId != original.EntryNodeId)
            errors.Add(new(DraftError.EntryChanged));

        var duplicated = draft.Nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicated.Count > 0) errors.Add(new(DraftError.DuplicateNodes, string.Join(", ", duplicated.Take(5))));

        var draftNodes = draft.Nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var node in original.Nodes)
        {
            if (!draftNodes.TryGetValue(node.Id, out var edited))
            {
                errors.Add(new(DraftError.CodeStepMissing, node.Label));
                continue;
            }
            if (edited.Kind != node.Kind || edited.Label != node.Label || edited.Location != node.Location)
                errors.Add(new(DraftError.CodeStepAltered, node.Label));
            if (edited.Change == ChangeKind.Added)
                errors.Add(new(DraftError.CodeStepMarkedNew, node.Label));
            if (edited.Change == ChangeKind.Modified && string.IsNullOrWhiteSpace(edited.Intent))
                errors.Add(new(DraftError.CodeStepModifiedNoIntent, node.Label));
        }

        var added = draft.Nodes.Where(n => !original.Nodes.Any(o => o.Id == n.Id)).ToList();
        if (added.Count > MaxAddedNodes) errors.Add(new(DraftError.TooManyAdded, MaxAddedNodes.ToString(CultureInfo.InvariantCulture)));
        foreach (var node in added)
        {
            if (!node.Id.StartsWith(AddedPrefix, StringComparison.Ordinal) || node.Change != ChangeKind.Added)
                errors.Add(new(DraftError.NewStepBadId, node.Label, AddedPrefix));
            if (string.IsNullOrWhiteSpace(node.Label) || node.Label.Length > MaxLabelLength)
                errors.Add(new(DraftError.NewStepNoName, MaxLabelLength.ToString(CultureInfo.InvariantCulture)));
            if (string.IsNullOrWhiteSpace(node.Intent))
                errors.Add(new(DraftError.NewStepNoIntent, node.Label));
            if (node.Kind is not (NodeKind.Method or NodeKind.Decision))
                errors.Add(new(DraftError.NewStepBadKind, node.Label));
        }

        foreach (var node in draft.Nodes.Where(n => n.Intent is { Length: > MaxIntentLength }))
            errors.Add(new(DraftError.IntentTooLong, node.Label, MaxIntentLength.ToString(CultureInfo.InvariantCulture)));

        foreach (var edge in original.Edges)
        {
            var kept = draft.Edges.FirstOrDefault(e => e.From == edge.From && e.To == edge.To && e.Label == edge.Label);
            if (kept is null)
                errors.Add(new(DraftError.EdgeMissing, Label(original, edge.From), Label(original, edge.To)));
            else if (kept.Change == ChangeKind.Added)
                errors.Add(new(DraftError.EdgeMarkedNew, Label(original, edge.From), Label(original, edge.To)));
        }

        foreach (var edge in draft.Edges.Where(e => e.Change == ChangeKind.Added))
            if (!draftNodes.ContainsKey(edge.From) || !draftNodes.ContainsKey(edge.To))
                errors.Add(new(DraftError.EdgeDangling));

        return errors;
    }

    public static bool HasChanges(FlowGraph draft) =>
        draft.Nodes.Any(n => n.Change is not null) || draft.Edges.Any(e => e.Change is not null);

    private static string Label(FlowGraph graph, string id) => graph.Nodes.FirstOrDefault(n => n.Id == id)?.Label ?? id;
}
