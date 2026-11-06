using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Infrastructure.Database.Live;

// O catálogo do banco vivo como mais uma fonte do esquema (Fase 40b). Nulo sem conexão cadastrada.
public interface ILiveSchemaSource
{
    Task<SchemaFragment?> ReadAsync(string repository, CancellationToken ct);
}

public enum LivePlanOutcome
{
    Ok = 0,
    NoConnection = 1,
    NotAllowed = 2,
    DatabaseError = 3
}

public record LivePlanResult(LivePlanOutcome Outcome, QueryPlan? Plan = null, string? Reason = null);

// Banco vivo de um repositório (Fase 40b, ADR-006): o teste da conexão, o catálogo para o esquema e o
// plano estimado de uma consulta. Falha do banco vira mensagem para a tela (sem segredo) e log com o
// número do erro; a string de conexão e o texto da consulta nunca vão para o log.
public class LiveDatabaseService(IServiceScopeFactory scopes, ILiveDatabase database, ILogger<LiveDatabaseService> logger) : ILiveSchemaSource
{
    public async Task<SchemaFragment?> ReadAsync(string repository, CancellationToken ct)
    {
        var connection = await ConnectionStringAsync(repository, ct);
        if (connection is null) return null;
        try
        {
            var fragment = await database.ReadCatalogAsync(connection, ct);
            logger.LogInformation("Catálogo do banco vivo do repositório {Repository} lido: {Tables} tabelas.", repository, fragment.Tables.Count);
            return fragment;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            logger.LogWarning(ex, "O banco vivo do repositório {Repository} não respondeu ao catálogo.", repository);
            return SchemaFragment.Empty with { Notes = [new SchemaNote($"O banco vivo não respondeu ({Reason(ex)}): o esquema saiu só do código.", string.Empty, 0)] };
        }
    }

    // Nulo sem conexão cadastrada.
    public async Task<LiveConnectionTest?> TestAsync(string repository, CancellationToken ct)
    {
        var connection = await ConnectionStringAsync(repository, ct);
        if (connection is null) return null;
        try
        {
            var tables = await database.CountTablesAsync(connection, ct);
            return new LiveConnectionTest(true, tables == 1 ? "Conectou: 1 tabela no catálogo." : $"Conectou: {tables} tabelas no catálogo.");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            logger.LogWarning(ex, "Teste da conexão com o banco vivo do repositório {Repository} falhou.", repository);
            return new LiveConnectionTest(false, $"Não conectou: {Reason(ex)}.");
        }
    }

    public async Task<LivePlanResult> PlanAsync(string repository, string sql, CancellationToken ct)
    {
        var connection = await ConnectionStringAsync(repository, ct);
        if (connection is null) return new(LivePlanOutcome.NoConnection);

        var guard = TSqlPlanGuard.Check(sql);
        if (!guard.Allowed) return new(LivePlanOutcome.NotAllowed, Reason: guard.Reason);

        try
        {
            var plan = ShowPlanReader.Read(await database.ShowPlanXmlAsync(connection, sql, guard.Variables, ct));
            logger.LogInformation("Plano estimado pedido ao banco vivo do repositório {Repository}: custo {Cost}, {Missing} índices faltando.",
                repository, plan.TotalCost, plan.MissingIndexes.Count);
            return new(LivePlanOutcome.Ok, plan);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException or System.Xml.XmlException)
        {
            logger.LogWarning(ex, "Plano estimado do banco vivo do repositório {Repository} falhou.", repository);
            return new(LivePlanOutcome.DatabaseError, Reason: Reason(ex));
        }
    }

    // Custo estimado antes e depois de uma reescrita (Fase 41): dois pedidos de plano, sem executar nada.
    // Nulo sem conexão cadastrada.
    public async Task<PlanComparison?> ComparePlansAsync(string repository, string before, string after, CancellationToken ct)
    {
        if (await ConnectionStringAsync(repository, ct) is null) return null;
        var original = await PlanAsync(repository, before, ct);
        var rewritten = await PlanAsync(repository, after, ct);
        var problem = original.Outcome != LivePlanOutcome.Ok
            ? $"O plano da consulta original não veio: {Problem(original)}"
            : rewritten.Outcome != LivePlanOutcome.Ok ? $"O plano da consulta reescrita não veio: {Problem(rewritten)}" : null;
        return new PlanComparison(original.Plan?.TotalCost, rewritten.Plan?.TotalCost, problem);
    }

    private static string Problem(LivePlanResult result) => result.Outcome switch
    {
        LivePlanOutcome.NoConnection => "a conexão com o banco vivo foi removida.",
        _ => (result.Reason ?? "o banco vivo não respondeu").TrimEnd('.') + "."
    };

    private async Task<string?> ConnectionStringAsync(string repository, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LiveConnectionStore>().ConnectionStringAsync(repository, ct);
    }

    // O motivo para a tela, sem a mensagem crua do driver (que pode citar usuário e servidor).
    private static string Reason(Exception ex) => ex switch
    {
        SqlException { Number: 18456 } => "usuário ou senha recusados",
        SqlException { Number: 4060 } => "o banco informado não existe ou o usuário não tem acesso a ele",
        // O SQL Server só mostra o plano a quem pode rodar a instrução: além do SHOWPLAN, o SELECT (ou o
        // INSERT, UPDATE e DELETE) nas tabelas da consulta. O Rastro nunca executa; a permissão é do banco.
        SqlException { Number: 229 or 230 } => "o usuário não tem permissão na tabela da consulta: o SQL Server só mostra o plano a quem pode rodar a instrução (SELECT nas tabelas, além do SHOWPLAN)",
        SqlException { Number: 262 or 297 or 300 } => "o usuário não tem a permissão VIEW DEFINITION ou SHOWPLAN",
        SqlException { Number: -2 } or TimeoutException => "tempo esgotado",
        SqlException { Number: 53 or -1 or 2 or 11001 or 10060 or 10061 } => "servidor não encontrado ou fora do ar",
        SqlException { Class: >= 11 and <= 16 } sql => $"o banco recusou a consulta (erro {sql.Number})",
        SqlException sql => $"erro {sql.Number} do SQL Server",
        System.Xml.XmlException => "o plano devolvido não pôde ser lido",
        _ => "falha na conexão"
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
        services.AddSingleton<LiveDatabaseService>();
        services.AddSingleton<ILiveSchemaSource>(sp => sp.GetRequiredService<LiveDatabaseService>());
        return services;
    }
}
