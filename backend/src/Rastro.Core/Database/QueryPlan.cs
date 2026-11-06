namespace Rastro.Core.Database;

// Plano estimado de uma consulta pelo banco vivo (Fase 40b, ADR-006): o que o próprio banco diz que
// faria, sem executar a consulta (SET SHOWPLAN_XML ON no SQL Server; EXPLAIN sem ANALYZE no PostgreSQL,
// Fase 42b). Nenhum dado das tabelas é lido.

// Um operador do plano. Cost: o custo estimado do próprio operador (o da subárvore menos o dos filhos).
// Percent: a parte do custo total da consulta (0 a 100). Object: a tabela, ou "tabela.índice".
// Scan: varredura de tabela ou de índice (lê tudo).
public record PlanOperation(string PhysicalOp, string LogicalOp, string? Object, double Cost, double Percent, double EstimatedRows, bool Scan);

// Índice que o próprio banco aponta como faltando. Impact: o ganho estimado (0 a 100).
public record MissingIndex(string Table, IReadOnlyList<string> Equality, IReadOnlyList<string> Inequality, IReadOnlyList<string> Include, double Impact);

// Operations: as mais caras primeiro (no máximo MaxOperations). Warnings: avisos do plano, em pt-BR.
public record QueryPlan(double TotalCost, IReadOnlyList<PlanOperation> Operations, IReadOnlyList<MissingIndex> MissingIndexes, IReadOnlyList<string> Warnings)
{
    public const int MaxOperations = 8;
}

// O que a tela sabe da conexão com o banco vivo: nunca a string, só se ela existe, o servidor, o banco e
// o dialeto (Fase 42b: tirado da própria string, ou do repositório quando a string serve aos dois).
public record LiveConnectionView(bool Configured, string? Server, string? Database, DateTimeOffset? UpdatedAt, SqlDialect Dialect = SqlDialect.SqlServer);

public record LiveConnectionTest(bool Ok, string Message);
