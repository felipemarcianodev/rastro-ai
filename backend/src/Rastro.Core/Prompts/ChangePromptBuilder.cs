using System.Text;
using Rastro.Core.Graph;
using Rastro.Core.Localization;

namespace Rastro.Core.Prompts;

public record PromptContext(string Repository, string SolutionPath, string EntryPoint, string? CommitHash);

// Linhas que os prompts e a documentação repetem, no idioma pedido.
public static class PromptText
{
    // "Repositório `x`, solution `y`." com o commit curto quando o levantamento tem um.
    public static string RepositoryLine(PromptContext context, UiLanguage language, string? commit = null)
    {
        commit ??= context.CommitHash;
        return commit is null
            ? LocalizedText.Get("Prompt.Common.RepositoryLine", language, context.Repository, context.SolutionPath)
            : LocalizedText.Get("Prompt.Common.RepositoryLineCommit", language, context.Repository, context.SolutionPath, commit[..Math.Min(7, commit.Length)]);
    }
}

// Monta o prompt da mudança a partir do rascunho (Fase 8). Sem IA: o mesmo rascunho gera sempre o
// mesmo texto no mesmo idioma (ADR-009: o idioma é parâmetro, nunca a cultura da thread). Âncoras
// exatas (arquivo:linha, símbolo), o que não mexer e critérios de aceite que o Rastro consegue
// conferir depois (Fase 9).
public static class ChangePromptBuilder
{
    public static string Build(PromptContext context, UiLanguage language, FlowGraph original, FlowGraph draft)
    {
        string T(string key, params object[] args) => LocalizedText.Get(key, language, args);

        var changes = DraftDiff.Compute(original, draft);
        var text = new StringBuilder();

        text.AppendLine(T("Prompt.Change.Title", context.EntryPoint));
        text.AppendLine();
        text.AppendLine(PromptText.RepositoryLine(context, language));
        text.AppendLine(T("Prompt.Change.Intro"));
        text.AppendLine();

        if (changes.Count == 0)
        {
            text.AppendLine(T("Prompt.Change.NoChanges"));
            return text.ToString();
        }

        text.AppendLine(T("Prompt.Change.Do.Heading"));
        var index = 0;
        foreach (var change in changes)
        {
            text.AppendLine();
            index++;
            switch (change.Type)
            {
                case DraftChangeType.AddedStep:
                    text.AppendLine(T("Prompt.Change.Added.Title", index, change.Node.Label, KindText(change.Node.Kind, language)));
                    text.AppendLine(T("Prompt.Change.Added.Intent", change.Node.Intent ?? ""));
                    if (change.CalledFrom.Count > 0)
                        text.AppendLine(T("Prompt.Change.Added.Where", Join(change.CalledFrom), BranchOf(draft, change.CalledFrom, change.Node, language)));
                    if (change.Calls.Count > 0) text.AppendLine(T("Prompt.Change.Added.After", Join(change.Calls)));
                    text.AppendLine(T("Prompt.Change.Added.Create"));
                    break;
                case DraftChangeType.ModifiedStep:
                    text.AppendLine(T("Prompt.Change.Modified.Title", index, change.Node.Label, Anchor(change.Node, language)));
                    if (change.Node.Rule is not null) text.AppendLine(T("Prompt.Change.Modified.Today", change.Node.Rule.Text));
                    if (change.Node.Condition is not null) text.AppendLine(T("Prompt.Change.Modified.Condition", change.Node.Condition));
                    text.AppendLine(T("Prompt.Change.Modified.Intent", change.Node.Intent ?? ""));
                    break;
                case DraftChangeType.RemovedStep:
                    text.AppendLine(T("Prompt.Change.Removed.Title", index, change.Node.Label, Anchor(change.Node, language)));
                    if (change.CalledFrom.Count > 0) text.AppendLine(T("Prompt.Change.Removed.CalledBy", Join(change.CalledFrom)));
                    if (change.Node.Intent is not null) text.AppendLine(T("Prompt.Change.Removed.Note", change.Node.Intent));
                    text.AppendLine(T("Prompt.Change.Removed.Shared"));
                    break;
                case DraftChangeType.MovedStep:
                    text.AppendLine(T("Prompt.Change.Moved.Title", index, change.Node.Label, Anchor(change.Node, language)));
                    text.AppendLine(T("Prompt.Change.Moved.Stops", Ref(change.PreviousCaller!)));
                    text.AppendLine(change.Branch is null
                        ? T("Prompt.Change.Moved.Starts", Join(change.CalledFrom))
                        : T("Prompt.Change.Moved.StartsBranch", Join(change.CalledFrom), change.Branch));
                    break;
                case DraftChangeType.AddedLink:
                    text.AppendLine(T("Prompt.Change.LinkAdded", index, Ref(change.Node), Ref(change.Other!), BranchParen(change.Branch, language)));
                    break;
                case DraftChangeType.RemovedLink:
                    text.AppendLine(T("Prompt.Change.LinkRemoved", index, Ref(change.Node), Ref(change.Other!), BranchParen(change.Branch, language)));
                    break;
            }
        }

        text.AppendLine();
        text.AppendLine(T("Prompt.Change.Keep.Heading"));
        var neighbours = Neighbours(draft, changes);
        if (neighbours.Count > 0)
            text.AppendLine(T("Prompt.Change.Keep.Neighbours", Join(neighbours)));
        text.AppendLine(T("Prompt.Change.Keep.Rest"));
        text.AppendLine(T("Prompt.Change.Keep.Tests"));

        text.AppendLine();
        text.AppendLine(T("Prompt.Change.Accept.Heading"));
        text.AppendLine(T("Prompt.Change.Accept.Resurvey", context.EntryPoint));
        foreach (var line in Expectations(draft, language)) text.AppendLine($"  - {line}");
        text.AppendLine(T("Prompt.Change.Accept.Build"));
        text.AppendLine(T("Prompt.Change.Accept.Validate"));
        return text.ToString();
    }

    // O que o Rastro vai conferir: ligações novas presentes e ligações removidas ausentes.
    public static IReadOnlyList<string> Expectations(FlowGraph draft, UiLanguage language)
    {
        string T(string key, params object[] args) => LocalizedText.Get(key, language, args);

        var nodes = draft.Nodes.ToDictionary(n => n.Id);
        var lines = new List<string>();
        foreach (var edge in draft.Edges.Where(e => e.Change == ChangeKind.Added))
            lines.Add(T("Prompt.Change.Expect.HaveCall", nodes[edge.From].Label, nodes[edge.To].Label) + (edge.Label is null ? "" : T("Prompt.Change.Expect.InBranch", edge.Label)));
        foreach (var edge in draft.Edges.Where(e => e.Change == ChangeKind.Removed))
            lines.Add(T("Prompt.Change.Expect.NotHaveCall", nodes[edge.From].Label, nodes[edge.To].Label) + (edge.Label is null ? "" : T("Prompt.Change.Expect.InBranch", edge.Label)));
        foreach (var node in draft.Nodes.Where(n => n.Change == ChangeKind.Modified))
            lines.Add(T("Prompt.Change.Expect.Keep", node.Label));
        return lines;
    }

    private static List<FlowNode> Neighbours(FlowGraph draft, IReadOnlyList<DraftChange> changes)
    {
        var changed = changes.Select(c => c.Node.Id).ToHashSet();
        return changes
            .SelectMany(c => c.CalledFrom.Concat(c.Calls).Append(c.PreviousCaller).Append(c.Other))
            .OfType<FlowNode>()
            .Where(n => n.Change is null && !changed.Contains(n.Id))
            .DistinctBy(n => n.Id)
            .ToList();
    }

    private static string BranchOf(FlowGraph draft, IReadOnlyList<FlowNode> callers, FlowNode node, UiLanguage language)
    {
        var label = draft.Edges.FirstOrDefault(e => e.To == node.Id && callers.Any(c => c.Id == e.From))?.Label;
        return label is null ? "" : LocalizedText.Get("Prompt.Change.BranchSuffix", language, label);
    }

    private static string BranchParen(string? branch, UiLanguage language) =>
        branch is null ? "" : LocalizedText.Get("Prompt.Change.BranchParen", language, branch);

    private static string Join(IEnumerable<FlowNode> nodes) => string.Join(", ", nodes.Select(Ref));

    private static string Ref(FlowNode node) =>
        node.Location is null ? $"`{node.Label}`" : $"`{node.Label}` ({node.Location.File}:{node.Location.Line})";

    private static string Anchor(FlowNode node, UiLanguage language)
    {
        if (node.Location is null) return "";
        return LocalizedText.Get("Prompt.Change.Anchor", language, node.Location.File, node.Location.Line)
               + (node.Symbol is null ? "" : LocalizedText.Get("Prompt.Change.AnchorSymbol", language, node.Symbol));
    }

    private static string KindText(NodeKind kind, UiLanguage language) =>
        LocalizedText.Get(kind == NodeKind.Decision ? "Prompt.Change.Kind.Decision" : "Prompt.Change.Kind.Method", language);
}
