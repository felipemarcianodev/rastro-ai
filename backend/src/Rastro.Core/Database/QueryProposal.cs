using Rastro.Core.Graph;

namespace Rastro.Core.Database;

// Proposta de melhora de uma consulta (Fase 41, ADR-001 e ADR-006): a consulta reescrita e os índices
// sugeridos por um agente (MCP) ou por um provedor (Rastro.Ai). O núcleo não conhece a IA: só recebe a
// proposta e a confere, de forma determinística, antes de qualquer coisa aparecer na tela. Nada aqui
// aplica a proposta nem executa a consulta: quem copia é o usuário.

// What: o que mudou; Why: por quê (o motivo de cada mudança).
public record QueryChange(string What, string Why);

// Índice sugerido: as colunas da chave na ordem, as colunas incluídas e o motivo.
public record IndexSuggestion(string Table, IReadOnlyList<string> Columns, IReadOnlyList<string> Include, bool Unique, string Reason);

// RewrittenSql vazio ou nulo: a IA não reescreveu a consulta.
public record QueryProposal(string? RewrittenSql, IReadOnlyList<QueryChange> Changes, IReadOnlyList<IndexSuggestion> Indexes, ProposalOrigin Origin);

// Resultado da conferência da consulta reescrita. Refusals: os motivos em pt-BR quando recusada.
// After: os achados sem IA da consulta reescrita, só quando aceita.
public record RewriteReview(string Sql, bool Accepted, IReadOnlyList<string> Refusals, QueryAnalysis? After);

// Resultado da conferência de um índice. CreateSql: o comando gerado pelo Rastro (não pela IA), só quando aceito.
public record IndexReview(
    string Table,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> Include,
    bool Unique,
    string Reason,
    bool Accepted,
    string? Refusal,
    string? CreateSql);

// Custo estimado pelo banco vivo antes e depois (Fase 41), sem executar a consulta. Problem: por que um dos
// dois planos não veio.
public record PlanComparison(double? CostBefore, double? CostAfter, string? Problem);

// Rewrite nulo: a IA não reescreveu. Plan nulo: sem banco vivo ou sem reescrita aceita.
public record QueryProposalReview(
    ProposalOrigin Origin,
    int FindingsBefore,
    RewriteReview? Rewrite,
    IReadOnlyList<QueryChange> Changes,
    IReadOnlyList<IndexReview> Indexes,
    PlanComparison? Plan = null);

public static class QueryProposalLimits
{
    public const int MaxChanges = 8;
    public const int MaxIndexes = 5;
    public const int MaxText = 600;
}
