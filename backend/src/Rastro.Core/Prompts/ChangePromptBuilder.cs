using System.Text;
using Rastro.Core.Graph;

namespace Rastro.Core.Prompts;

public record PromptContext(string Repository, string SolutionPath, string EntryPoint, string? CommitHash);

// Monta o prompt da mudança a partir do rascunho (Fase 8). Sem IA: o mesmo rascunho gera sempre o
// mesmo texto. Âncoras exatas (arquivo:linha, símbolo), o que não mexer e critérios de aceite que o
// Rastro consegue conferir depois (Fase 9).
public static class ChangePromptBuilder
{
    public static string Build(PromptContext context, FlowGraph original, FlowGraph draft)
    {
        var changes = DraftDiff.Compute(original, draft);
        var text = new StringBuilder();

        text.AppendLine($"# Mudança no fluxo `{context.EntryPoint}`");
        text.AppendLine();
        text.AppendLine($"Repositório `{context.Repository}`, solution `{context.SolutionPath}`" +
                        (context.CommitHash is null ? "." : $", levantado no commit `{context.CommitHash[..Math.Min(7, context.CommitHash.Length)]}`."));
        text.AppendLine("Faça só as mudanças abaixo. O fluxo foi levantado pelo compilador (Roslyn); as posições são do código atual.");
        text.AppendLine();

        if (changes.Count == 0)
        {
            text.AppendLine("Nenhuma mudança no rascunho.");
            return text.ToString();
        }

        text.AppendLine("## O que mudar");
        var index = 0;
        foreach (var change in changes)
        {
            text.AppendLine();
            index++;
            switch (change.Type)
            {
                case DraftChangeType.AddedStep:
                    text.AppendLine($"### {index}. Criar o passo `{change.Node.Label}` ({KindText(change.Node.Kind)})");
                    text.AppendLine($"- O que deve fazer: {change.Node.Intent}");
                    if (change.CalledFrom.Count > 0)
                        text.AppendLine($"- Onde entra: chamado por {Join(change.CalledFrom)}" + BranchOf(draft, change.CalledFrom, change.Node) + ".");
                    if (change.Calls.Count > 0) text.AppendLine($"- Depois dele: {Join(change.Calls)}.");
                    text.AppendLine("- Crie o código onde fizer sentido na arquitetura atual, perto de quem o chama.");
                    break;
                case DraftChangeType.ModifiedStep:
                    text.AppendLine($"### {index}. Mudar `{change.Node.Label}`{Anchor(change.Node)}");
                    if (change.Node.Rule is not null) text.AppendLine($"- Como funciona hoje: {change.Node.Rule.Text}");
                    if (change.Node.Condition is not null) text.AppendLine($"- Condição atual: `{change.Node.Condition}`");
                    text.AppendLine($"- O que deve mudar: {change.Node.Intent}");
                    break;
                case DraftChangeType.RemovedStep:
                    text.AppendLine($"### {index}. Tirar `{change.Node.Label}` do fluxo{Anchor(change.Node)}");
                    if (change.CalledFrom.Count > 0) text.AppendLine($"- Hoje é chamado por {Join(change.CalledFrom)}; essas chamadas deixam de existir neste fluxo.");
                    if (change.Node.Intent is not null) text.AppendLine($"- Observação: {change.Node.Intent}");
                    text.AppendLine("- Se o método for usado por outros fluxos, não apague o método: só tire a chamada deste fluxo.");
                    break;
                case DraftChangeType.MovedStep:
                    text.AppendLine($"### {index}. Mover `{change.Node.Label}`{Anchor(change.Node)}");
                    text.AppendLine($"- Deixa de ser chamado por {Ref(change.PreviousCaller!)}.");
                    text.AppendLine($"- Passa a ser chamado por {Join(change.CalledFrom)}" + (change.Branch is null ? "." : $", no ramo `{change.Branch}`."));
                    break;
                case DraftChangeType.AddedLink:
                    text.AppendLine($"### {index}. {Ref(change.Node)} passa a chamar {Ref(change.Other!)}" + (change.Branch is null ? "" : $" (ramo `{change.Branch}`)"));
                    break;
                case DraftChangeType.RemovedLink:
                    text.AppendLine($"### {index}. {Ref(change.Node)} deixa de chamar {Ref(change.Other!)}" + (change.Branch is null ? "" : $" (ramo `{change.Branch}`)"));
                    break;
            }
        }

        text.AppendLine();
        text.AppendLine("## O que não mexer");
        var neighbours = Neighbours(draft, changes);
        if (neighbours.Count > 0)
            text.AppendLine($"- Os passos em volta das mudanças continuam como estão: {Join(neighbours)}.");
        text.AppendLine("- O resto do fluxo não muda. Não altere assinaturas públicas nem contratos de API sem necessidade; se precisar, explique por quê.");
        text.AppendLine("- Não apague testes. Ajuste ou crie testes para o comportamento novo.");

        text.AppendLine();
        text.AppendLine("## Critérios de aceite");
        text.AppendLine($"- Levantado de novo pelo Rastro, o fluxo de `{context.EntryPoint}` deve:");
        foreach (var line in Expectations(draft)) text.AppendLine($"  - {line}");
        text.AppendLine("- Build e testes do repositório passando.");
        text.AppendLine("- Ao terminar, peça a validação ao Rastro (ferramenta `check_change` no MCP, ou \"Validar mudança\" na tela).");
        return text.ToString();
    }

    // O que o Rastro vai conferir: ligações novas presentes e ligações removidas ausentes.
    public static IReadOnlyList<string> Expectations(FlowGraph draft)
    {
        var nodes = draft.Nodes.ToDictionary(n => n.Id);
        var lines = new List<string>();
        foreach (var edge in draft.Edges.Where(e => e.Change == ChangeKind.Added))
            lines.Add($"ter `{nodes[edge.From].Label}` chamando `{nodes[edge.To].Label}`" + (edge.Label is null ? "" : $" no ramo `{edge.Label}`"));
        foreach (var edge in draft.Edges.Where(e => e.Change == ChangeKind.Removed))
            lines.Add($"não ter `{nodes[edge.From].Label}` chamando `{nodes[edge.To].Label}`" + (edge.Label is null ? "" : $" no ramo `{edge.Label}`"));
        foreach (var node in draft.Nodes.Where(n => n.Change == ChangeKind.Modified))
            lines.Add($"manter `{node.Label}` no fluxo, com a regra nova");
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

    private static string BranchOf(FlowGraph draft, IReadOnlyList<FlowNode> callers, FlowNode node)
    {
        var label = draft.Edges.FirstOrDefault(e => e.To == node.Id && callers.Any(c => c.Id == e.From))?.Label;
        return label is null ? "" : $", no ramo `{label}`";
    }

    private static string Join(IEnumerable<FlowNode> nodes) => string.Join(", ", nodes.Select(Ref));

    private static string Ref(FlowNode node) =>
        node.Location is null ? $"`{node.Label}`" : $"`{node.Label}` ({node.Location.File}:{node.Location.Line})";

    private static string Anchor(FlowNode node) =>
        node.Location is null ? "" : $" em `{node.Location.File}:{node.Location.Line}`" + (node.Symbol is null ? "" : $" (`{node.Symbol}`)");

    private static string KindText(NodeKind kind) => kind == NodeKind.Decision ? "decisão" : "método";
}
