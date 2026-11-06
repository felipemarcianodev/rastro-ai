using System.Text.RegularExpressions;
using SqlParser.Ast;
using Rastro.Core.Database;

namespace Rastro.Extractor.Database;

// A porta da consulta para o banco vivo em cada dialeto: o SQL Server pelo TSqlPlanGuard (Fase 40b) e o
// PostgreSQL pelo PgSqlPlanGuard (Fase 42).
public static class SqlPlanGuards
{
    public static PlanGuardResult Check(SqlDialect dialect, string sql) => dialect switch
    {
        SqlDialect.PostgreSql => PgSqlPlanGuard.Check(sql),
        _ => TSqlPlanGuard.Check(sql)
    };
}

// Porta da consulta para o banco vivo do PostgreSQL (Fase 42, ADR-006): antes do EXPLAIN (sempre sem
// ANALYZE), o leitor de SQL do PostgreSQL confere que o texto é uma instrução só, SELECT, INSERT, UPDATE,
// DELETE ou MERGE, sem SELECT INTO, sem escrita dentro do WITH e sem função de efeito colateral
// conhecida (pg_sleep, set_config, nextval, dblink, leitura de arquivo, trava). SET, COPY, CALL, DO,
// EXPLAIN e DDL nunca chegam ao banco. O EXPLAIN sem ANALYZE não executa a instrução; esta porta é a
// primeira camada, e a conexão só de leitura (default_transaction_read_only) é a segunda.
public static partial class PgSqlPlanGuard
{
    // Funções que mexem em algo fora da consulta (ou leem arquivo e dados por fora do plano).
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "pg_sleep", "pg_sleep_for", "pg_sleep_until", "pg_terminate_backend", "pg_cancel_backend", "pg_reload_conf",
        "pg_rotate_logfile", "pg_switch_wal", "pg_create_restore_point", "pg_promote", "pg_backup_start", "pg_backup_stop",
        "pg_start_backup", "pg_stop_backup", "set_config", "nextval", "setval", "lastval", "pg_notify",
        "pg_logical_emit_message", "pg_create_logical_replication_slot", "pg_create_physical_replication_slot",
        "pg_drop_replication_slot", "txid_current", "pg_current_xact_id", "query_to_xml", "query_to_xml_and_xmlschema",
        "cursor_to_xml", "table_to_xml", "table_to_xml_and_xmlschema", "schema_to_xml", "database_to_xml",
        "pg_read_file", "pg_read_binary_file", "pg_stat_file", "pg_import_system_collations", "pg_log_backend_memory_contexts",
        "pg_wal_replay_pause", "pg_wal_replay_resume", "pg_xact_commit_timestamp"
    };

    private static readonly string[] ForbiddenPrefixes =
        ["dblink", "lo_", "pg_advisory", "pg_try_advisory", "pg_file", "pg_ls_", "pg_stat_reset", "pg_replication_origin"];

    public static PlanGuardResult Check(string sql)
    {
        var statements = PgSqlText.Parse(sql, out var error);
        if (statements is null)
            return Deny($"O leitor de SQL do PostgreSQL não entendeu a consulta (linha {error?.Line ?? 1}): {error?.Message ?? "texto vazio"}.");
        if (statements.Count != 1)
            return Deny("O plano estimado sai para uma instrução só (sem ponto e vírgula separando outras).");

        var statement = statements[0];
        if (statement is not (Statement.Select or Statement.Insert or Statement.Update or Statement.Delete or Statement.Merge))
            return Deny("O plano estimado só sai para SELECT, INSERT, UPDATE, DELETE ou MERGE: SET, COPY, CALL, DO, EXPLAIN e DDL não vão ao banco.");

        var variables = new List<string>();
        foreach (var node in PgAst.Descendants(statement))
        {
            switch (node)
            {
                case Select { Into: not null }:
                    return Deny("SELECT INTO cria uma tabela: não vai ao banco vivo.");
                // O SELECT de um INSERT ... SELECT é uma instrução dentro da outra, e só lê.
                case SetExpression.Insert or Statement.Insert or Statement.Update or Statement.Delete or Statement.Merge:
                    return Deny("Instrução que grava dentro de outra (no WITH ou numa subconsulta): não vai ao banco vivo.");
                case Expression.Function function when Blocked(function.Name) is { } blocked:
                    return Deny($"A função {blocked} tem efeito fora da consulta: não vai ao banco vivo.");
                case TableFactor.Table { Args: not null } table when Blocked(table.Name) is { } tableFunction:
                    return Deny($"A função {tableFunction} tem efeito fora da consulta: não vai ao banco vivo.");
                case TableFactor.Function tableFunction when Blocked(tableFunction.Name) is { } other:
                    return Deny($"A função {other} tem efeito fora da consulta: não vai ao banco vivo.");
                case Expression.LiteralValue { Value: Value.Placeholder placeholder }:
                    variables.Add(placeholder.Value);
                    break;
                case Expression.UnaryOp { Op: UnaryOperator.PGAbs, Expression: Expression.Identifier id }:
                    variables.Add("@" + id.Ident.Value);
                    break;
            }
        }

        var invalid = variables.FirstOrDefault(v => !VariableName().IsMatch(v));
        if (invalid is not null) return Deny($"Parâmetro que o Rastro não declara: {invalid}.");
        return new PlanGuardResult(true, null, variables.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static string? Blocked(ObjectName? name)
    {
        if (name is null || name.Values.Count == 0) return null;
        var last = name.Values[^1].Value;
        return Forbidden.Contains(last) || ForbiddenPrefixes.Any(p => last.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ? last : null;
    }

    private static PlanGuardResult Deny(string reason) => new(false, reason, []);

    // $1 a $9999, ou @nome (o parâmetro do Npgsql e o @p0 que o Rastro tira do FromSql).
    [GeneratedRegex(@"^(\$[1-9][0-9]{0,3}|@[A-Za-z_][A-Za-z0-9_]{0,62})$")]
    private static partial Regex VariableName();
}
