using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Rastro.Core.Database;
using Rastro.Core.Localization;
using Txt = Rastro.Infrastructure.Resources.Database;
using Rastro.Extractor.Database;

namespace Rastro.Infrastructure.Database.Live;

// O catálogo do banco vivo como mais uma fonte do esquema (Fase 40b). Nulo sem conexão cadastrada.
public interface ILiveSchemaSource
{
    Task<SchemaFragment?> ReadAsync(string repository, UiLanguage language, CancellationToken ct);
}

public enum LivePlanOutcome
{
    Ok = 0,
    NoConnection = 1,
    NotAllowed = 2,
    DatabaseError = 3
}

// Reason: o motivo para a tela, já no idioma do pedido (ADR-009).
public record LivePlanResult(LivePlanOutcome Outcome, QueryPlan? Plan = null, string? Reason = null);

// Banco vivo de um repositório (Fase 40b, ADR-006): o teste da conexão, o catálogo para o esquema e o
// plano estimado de uma consulta. O driver e a porta da consulta saem do dialeto da conexão (o
// PostgreSQL na Fase 42b). Falha do banco vira mensagem para a tela (sem segredo) e log com o código do
// erro; a string de conexão e o texto da consulta nunca vão para o log.
public class LiveDatabaseService(IServiceScopeFactory scopes, IEnumerable<ILiveDatabase> databases, ILogger<LiveDatabaseService> logger) : ILiveSchemaSource
{
    private readonly Dictionary<SqlDialect, ILiveDatabase> _databases = databases.GroupBy(d => d.Dialect).ToDictionary(g => g.Key, g => g.Last());

    private ILiveDatabase Database(SqlDialect dialect) =>
        _databases.GetValueOrDefault(dialect) ?? throw new InvalidOperationException($"Sem driver do banco vivo para o dialeto {dialect}.");

    public async Task<SchemaFragment?> ReadAsync(string repository, UiLanguage language, CancellationToken ct)
    {
        var live = await ConnectionAsync(repository, ct);
        if (live is not { } connection) return null;
        try
        {
            var fragment = await Database(connection.Dialect).ReadCatalogAsync(connection.ConnectionString, ct);
            logger.LogInformation("Catálogo do banco vivo do repositório {Repository} lido: {Tables} tabelas.", repository, fragment.Tables.Count);
            return fragment;
        }
        catch (Exception ex) when (Expected(ex))
        {
            logger.LogWarning(ex, "O banco vivo do repositório {Repository} não respondeu ao catálogo.", repository);
            return SchemaFragment.Empty with { Notes = [new SchemaNote(Txt.Get("Live.CatalogNoAnswer", language, Reason(ex, language)), string.Empty, 0)] };
        }
    }

    // Nulo sem conexão cadastrada.
    public async Task<LiveConnectionTest?> TestAsync(string repository, UiLanguage language, CancellationToken ct)
    {
        var live = await ConnectionAsync(repository, ct);
        if (live is not { } connection) return null;
        try
        {
            var tables = await Database(connection.Dialect).CountTablesAsync(connection.ConnectionString, ct);
            return new LiveConnectionTest(true, tables == 1 ? Txt.Get("Live.TestOkOne", language) : Txt.Get("Live.TestOkMany", language, tables));
        }
        catch (Exception ex) when (Expected(ex))
        {
            logger.LogWarning(ex, "Teste da conexão com o banco vivo do repositório {Repository} falhou.", repository);
            return new LiveConnectionTest(false, Txt.Get("Live.TestFailed", language, Reason(ex, language)));
        }
    }

    public async Task<LivePlanResult> PlanAsync(string repository, string sql, UiLanguage language, CancellationToken ct)
    {
        var live = await ConnectionAsync(repository, ct);
        if (live is not { } connection) return new(LivePlanOutcome.NoConnection);

        // A porta do dialeto da conexão: o que o banco vai ler é o que ela conferiu.
        var guard = SqlPlanGuards.Check(connection.Dialect, sql, language);
        if (!guard.Allowed) return new(LivePlanOutcome.NotAllowed, Reason: guard.Reason);

        try
        {
            var raw = await Database(connection.Dialect).EstimatedPlanAsync(connection.ConnectionString, sql, guard.Variables, ct);
            var plan = connection.Dialect == SqlDialect.PostgreSql ? ExplainPlanReader.Read(raw) : ShowPlanReader.Read(raw, language);
            logger.LogInformation("Plano estimado pedido ao banco vivo do repositório {Repository}: custo {Cost}, {Missing} índices faltando.",
                repository, plan.TotalCost, plan.MissingIndexes.Count);
            return new(LivePlanOutcome.Ok, plan);
        }
        catch (Exception ex) when (Expected(ex))
        {
            logger.LogWarning(ex, "Plano estimado do banco vivo do repositório {Repository} falhou.", repository);
            return new(LivePlanOutcome.DatabaseError, Reason: Reason(ex, language));
        }
    }

    // Custo estimado antes e depois de uma reescrita (Fase 41): dois pedidos de plano, sem executar nada.
    // Nulo sem conexão cadastrada.
    public async Task<PlanComparison?> ComparePlansAsync(string repository, string before, string after, UiLanguage language, CancellationToken ct)
    {
        if (await ConnectionAsync(repository, ct) is null) return null;
        var original = await PlanAsync(repository, before, language, ct);
        var rewritten = await PlanAsync(repository, after, language, ct);
        var problem = original.Outcome != LivePlanOutcome.Ok
            ? Txt.Get("Live.OriginalPlanMissing", language, Problem(original, language))
            : rewritten.Outcome != LivePlanOutcome.Ok ? Txt.Get("Live.RewrittenPlanMissing", language, Problem(rewritten, language)) : null;
        return new PlanComparison(original.Plan?.TotalCost, rewritten.Plan?.TotalCost, problem);
    }

    private static string Problem(LivePlanResult result, UiLanguage language) => result.Outcome switch
    {
        LivePlanOutcome.NoConnection => Txt.Get("Live.ConnectionRemoved", language),
        _ => (result.Reason ?? Txt.Get("Live.NoAnswer", language)).TrimEnd('.') + "."
    };

    private async Task<(string ConnectionString, SqlDialect Dialect)?> ConnectionAsync(string repository, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LiveConnectionStore>().ConnectionAsync(repository, ct);
    }

    private static bool Expected(Exception ex) =>
        ex is SqlException or NpgsqlException or InvalidOperationException or TimeoutException or System.Xml.XmlException
            or JsonException or LivePlanUnavailableException;

    // O motivo para a tela, sem a mensagem crua do driver (que pode citar usuário e servidor).
    private static string Reason(Exception ex, UiLanguage language) => ex switch
    {
        SqlException { Number: 18456 } => Txt.Get("Live.Reason.BadCredentials", language),
        SqlException { Number: 4060 } => Txt.Get("Live.Reason.DatabaseMissingOrNoAccess", language),
        // O SQL Server só mostra o plano a quem pode rodar a instrução: além do SHOWPLAN, o SELECT (ou o
        // INSERT, UPDATE e DELETE) nas tabelas da consulta. O Rastro nunca executa; a permissão é do banco.
        SqlException { Number: 229 or 230 } => Txt.Get("Live.Reason.SqlNoPermission", language),
        SqlException { Number: 262 or 297 or 300 } => Txt.Get("Live.Reason.SqlNoViewDefinition", language),
        SqlException { Number: -2 } or TimeoutException => Txt.Get("Live.Reason.Timeout", language),
        SqlException { Number: 53 or -1 or 2 or 11001 or 10060 or 10061 } => Txt.Get("Live.Reason.ServerUnreachable", language),
        SqlException { Class: >= 11 and <= 16 } sql => Txt.Get("Live.Reason.DatabaseRefused", language, sql.Number),
        SqlException sql => Txt.Get("Live.Reason.SqlError", language, sql.Number),
        System.Xml.XmlException or JsonException => Txt.Get("Live.Reason.PlanUnreadable", language),
        LivePlanUnavailableException { PostgreSqlMajor: > 0 } unavailable => Txt.Get("Live.Reason.PgPlanNeeds12", language, unavailable.PostgreSqlMajor),
        LivePlanUnavailableException unavailable => unavailable.Message,
        // PostgreSQL (Fase 42b), pelo código do erro (SQLSTATE), nunca pela mensagem do servidor.
        PostgresException { SqlState: "28P01" or "28000" } => Txt.Get("Live.Reason.BadCredentials", language),
        PostgresException { SqlState: "3D000" } => Txt.Get("Live.Reason.PgDatabaseMissing", language),
        // O PostgreSQL confere a permissão da instrução também no EXPLAIN: o SELECT (ou o INSERT, UPDATE e
        // DELETE) na tabela da consulta. O Rastro nunca executa; a permissão é do banco.
        PostgresException { SqlState: "42501" } => Txt.Get("Live.Reason.PgNoPermission", language),
        PostgresException { SqlState: "57014" } => Txt.Get("Live.Reason.Timeout", language),
        PostgresException { SqlState: "25006" } => Txt.Get("Live.Reason.PgReadOnly", language),
        PostgresException { SqlState: "42P01" } => Txt.Get("Live.Reason.PgTableMissing", language),
        PostgresException { SqlState: "42601" } => Txt.Get("Live.Reason.PgSyntax", language),
        PostgresException pg => Txt.Get("Live.Reason.DatabaseRefused", language, pg.SqlState),
        NpgsqlException { InnerException: System.Net.Sockets.SocketException or TimeoutException } => Txt.Get("Live.Reason.ServerUnreachable", language),
        NpgsqlException { InnerException: System.IO.IOException } => Txt.Get("Live.Reason.ConnectionDropped", language),
        _ => Txt.Get("Live.Reason.ConnectionFailed", language)
    };
}

public static class LiveDatabaseServices
{
    // Só a API registra: o cadastro da conexão é pela tela, e o DataProtection (o mesmo da chave da IA,
    // com as chaves no banco local e o DPAPI no Windows) é registrado pelo host.
    public static IServiceCollection AddRastroLiveDatabase(this IServiceCollection services)
    {
        services.AddScoped<LiveConnectionStore>();
        services.AddSingleton<ILiveDatabase, SqlServerLiveDatabase>();
        services.AddSingleton<ILiveDatabase, NpgsqlLiveDatabase>();
        services.AddSingleton<LiveDatabaseService>();
        services.AddSingleton<ILiveSchemaSource>(sp => sp.GetRequiredService<LiveDatabaseService>());
        return services;
    }
}
