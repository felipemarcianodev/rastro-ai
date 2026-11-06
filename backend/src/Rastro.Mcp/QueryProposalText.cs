using System.ComponentModel;
using System.Text;
using Rastro.Core.Database;

namespace Rastro.Mcp;

// Entrada do propose_query_rewrite (Fase 41): o agente propõe, o núcleo confere.
public record QueryChangeInput(
    [property: Description("O que mudou.")] string? What,
    [property: Description("Por que mudou.")] string? Why);

public record QueryIndexInput(
    [property: Description("Tabela do índice, ex.: pedidos ou dbo.pedidos.")] string? Table,
    [property: Description("Colunas da chave, na ordem.")] string[]? Columns,
    [property: Description("Colunas incluídas (INCLUDE), opcional.")] string[]? Include,
    [property: Description("Índice único.")] bool Unique,
    [property: Description("Por que o índice ajuda esta consulta.")] string? Reason);

// A conferência da proposta em texto para o agente: o que passou, o que foi recusado e por quê.
public static class QueryProposalText
{
    public static string Format(QueryProposalReview review)
    {
        var text = new StringBuilder("Conferência da proposta (regra fixa, sem IA; nada foi aplicado nem executado).\n");
        switch (review.Rewrite)
        {
            case null:
                text.AppendLine("Reescrita: nenhuma (igual à original ou vazia).");
                break;
            case { Accepted: true, After: var after }:
                text.AppendLine($"Reescrita: aceita. Achados das regras fixas: {review.FindingsBefore} antes, {after?.Findings.Count ?? 0} depois.");
                if (after is { Findings.Count: > 0 }) text.AppendLine(QueryContextText.Findings(after));
                break;
            case var refused:
                text.AppendLine("Reescrita: recusada.");
                foreach (var reason in refused.Refusals) text.AppendLine($"- {reason}");
                break;
        }

        foreach (var index in review.Indexes)
            text.AppendLine(index.Accepted
                ? $"Índice aceito em {index.Table} ({string.Join(", ", index.Columns)}): {index.CreateSql}"
                : $"Índice recusado em {index.Table} ({string.Join(", ", index.Columns)}): {index.Refusal}");

        text.Append("Mostre ao usuário a consulta reescrita aceita e os comandos dos índices aceitos, para ele avaliar e copiar. O que foi recusado: corrija e proponha de novo, sem inventar tabela nem coluna. Aplicar é sempre do usuário.");
        return text.ToString();
    }
}
