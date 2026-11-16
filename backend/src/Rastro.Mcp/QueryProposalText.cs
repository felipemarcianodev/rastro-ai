using System.ComponentModel;
using System.Text;
using Rastro.Core.Database;
using Rastro.Core.Localization;

namespace Rastro.Mcp;

// Entrada do propose_query_rewrite (Fase 41): o agente propõe, o núcleo confere. Descrições em inglês fixo (ADR-009).
public record QueryChangeInput(
    [property: Description("What changed.")] string? What,
    [property: Description("Why it changed.")] string? Why);

public record QueryIndexInput(
    [property: Description("Table of the index, e.g. orders or dbo.orders.")] string? Table,
    [property: Description("Key columns, in order.")] string[]? Columns,
    [property: Description("Included columns (INCLUDE), optional.")] string[]? Include,
    [property: Description("Unique index.")] bool Unique,
    [property: Description("Why the index helps this query.")] string? Reason);

// A conferência da proposta em texto para o agente: o que passou, o que foi recusado e por quê, no idioma de quem usa.
public static class QueryProposalText
{
    public static string Format(QueryProposalReview review, UiLanguage language)
    {
        string T(string key, params object[] args) => McpText.Get(key, language, args);

        var text = new StringBuilder(T("Proposal.Header") + "\n");
        switch (review.Rewrite)
        {
            case null:
                text.AppendLine(T("Proposal.NoRewrite"));
                break;
            case { Accepted: true, After: var after }:
                text.AppendLine(T("Proposal.RewriteAccepted", review.FindingsBefore, after?.Findings.Count ?? 0));
                if (after is { Findings.Count: > 0 }) text.AppendLine(QueryContextText.Findings(after, language));
                break;
            case var refused:
                text.AppendLine(T("Proposal.RewriteRefused"));
                foreach (var reason in refused.Refusals) text.AppendLine($"- {reason}");
                break;
        }

        foreach (var index in review.Indexes)
            text.AppendLine(index.Accepted
                ? T("Proposal.IndexAccepted", index.Table, string.Join(", ", index.Columns), index.CreateSql ?? string.Empty)
                : T("Proposal.IndexRefused", index.Table, string.Join(", ", index.Columns), index.Refusal ?? string.Empty));

        if (review.Plan is { } plan)
            text.AppendLine(plan.Problem is not null
                ? T("Proposal.CostProblem", plan.Problem)
                : T("Proposal.Cost", QueryPlanText.Cost(plan.CostBefore ?? 0, language), QueryPlanText.Cost(plan.CostAfter ?? 0, language)));

        text.Append(T("Proposal.Footer"));
        return text.ToString();
    }
}
