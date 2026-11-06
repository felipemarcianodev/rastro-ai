using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rastro.Core.Database;

namespace Rastro.Infrastructure.Database.Live;

// A conexão com o banco vivo de um repositório (Fase 40b, ADR-006). A string fica cifrada com o
// DataProtection, num propósito próprio, e só sai daqui para abrir a conexão: nunca volta na resposta,
// nunca vai para o log e nunca vai para a IA. Server, Database e o dialeto ficam à parte, para a tela.
public class LiveConnection
{
    public string Repository { get; set; } = string.Empty;
    public string ConnectionProtected { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string? Database { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Fase 42b: qual driver abre a conexão.
    public SqlDialect Dialect { get; set; } = SqlDialect.SqlServer;
}

// O dialeto de uma string de conexão (Fase 42b), sem perguntar ao usuário: as chaves que só o Npgsql
// aceita (Host, Port, Username, SSL Mode) dizem PostgreSQL, as que só o SqlClient aceita (Data Source,
// Initial Catalog, Integrated Security, TrustServerCertificate) dizem SQL Server. "Server=x;Database=y;
// User Id=z" serve aos dois: aí vale o dialeto do repositório.
public static class LiveConnectionDialect
{
    private static readonly HashSet<string> PostgreSqlKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "host", "port", "username", "sslmode", "searchpath", "includeerrordetail", "servercompatibilitymode",
        "options", "targetsessionattributes", "rootcertificate", "sslcertificate", "sslkey", "sslpassword", "passfile"
    };

    private static readonly HashSet<string> SqlServerKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "datasource", "initialcatalog", "integratedsecurity", "trusted_connection", "trustservercertificate", "encrypt",
        "multipleactiveresultsets", "applicationintent", "attachdbfilename", "addr", "address", "networkaddress",
        "failoverpartner", "multisubnetfailover", "authentication", "persistsecurityinfo"
    };

    // Nulo quando a string nem é chave=valor.
    public static SqlDialect? Detect(string connectionString, SqlDialect repository)
    {
        if (connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return SqlDialect.PostgreSql;
        DbConnectionStringBuilder builder;
        try
        {
            builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch (ArgumentException)
        {
            return null;
        }
        var keys = builder.Keys.Cast<string>().Select(k => k.Replace(" ", string.Empty)).ToList();
        var postgreSql = keys.Count(PostgreSqlKeys.Contains);
        var sqlServer = keys.Count(SqlServerKeys.Contains);
        if (postgreSql != sqlServer) return postgreSql > sqlServer ? SqlDialect.PostgreSql : SqlDialect.SqlServer;
        return repository;
    }
}

public class LiveConnectionStore(RastroDbContext db, IDataProtectionProvider protection, TimeProvider clock)
{
    private const string Purpose = "Rastro.Database.LiveConnection";
    public const int MaxLength = 2000;

    private IDataProtector Protector => protection.CreateProtector(Purpose);

    public async Task<LiveConnectionView> ViewAsync(string repository, CancellationToken ct) =>
        View(await db.LiveConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Repository == repository, ct));

    // ArgumentException: string vazia, longa demais, que o driver não entende ou sem servidor.
    // repositoryDialect: o dialeto do código do repositório, para a string que serve aos dois drivers.
    public async Task<LiveConnectionView> SaveAsync(string repository, string? connectionString, SqlDialect repositoryDialect, CancellationToken ct)
    {
        var text = connectionString?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > MaxLength) throw new ArgumentException("String de conexão vazia ou longa demais.");
        var dialect = LiveConnectionDialect.Detect(text, repositoryDialect)
                      ?? throw new ArgumentException("String de conexão que não está no formato chave=valor;chave=valor.");
        var (server, database) = dialect == SqlDialect.PostgreSql ? PostgreSql(text) : SqlServer(text);

        var entity = await db.LiveConnections.FirstOrDefaultAsync(c => c.Repository == repository, ct);
        if (entity is null)
        {
            entity = new LiveConnection { Repository = repository };
            db.LiveConnections.Add(entity);
        }
        entity.ConnectionProtected = Protector.Protect(text);
        entity.Server = server;
        entity.Database = string.IsNullOrWhiteSpace(database) ? null : database;
        entity.Dialect = dialect;
        entity.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return View(entity);
    }

    private static (string Server, string? Database) SqlServer(string text)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(text);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            // A mensagem do driver pode repetir um pedaço da string: não sai daqui.
            throw new ArgumentException("String de conexão que o driver do SQL Server não entende.");
        }
        if (string.IsNullOrWhiteSpace(builder.DataSource)) throw new ArgumentException("A string de conexão não diz o servidor (Data Source).");
        return (builder.DataSource, builder.InitialCatalog);
    }

    private static (string Server, string? Database) PostgreSql(string text)
    {
        if (text.Contains("://", StringComparison.Ordinal))
            throw new ArgumentException("O Rastro lê a string de conexão do PostgreSQL no formato chave=valor (Host=...;Database=...;Username=...;Password=...), não como URL.");
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(text);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            // A mensagem do driver pode repetir um pedaço da string: não sai daqui.
            throw new ArgumentException("String de conexão que o driver do PostgreSQL não entende.");
        }
        if (string.IsNullOrWhiteSpace(builder.Host)) throw new ArgumentException("A string de conexão não diz o servidor (Host).");
        var server = builder.Port is > 0 and not NpgsqlConnection.DefaultPort ? $"{builder.Host}:{builder.Port}" : builder.Host;
        return (server, builder.Database);
    }

    public async Task RemoveAsync(string repository, CancellationToken ct) =>
        await db.LiveConnections.Where(c => c.Repository == repository).ExecuteDeleteAsync(ct);

    // Nula sem conexão cadastrada.
    public async Task<(string ConnectionString, SqlDialect Dialect)?> ConnectionAsync(string repository, CancellationToken ct)
    {
        var entity = await db.LiveConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Repository == repository, ct);
        return entity is null ? null : (Protector.Unprotect(entity.ConnectionProtected), entity.Dialect);
    }

    // Nula sem conexão cadastrada.
    public async Task<string?> ConnectionStringAsync(string repository, CancellationToken ct) =>
        (await ConnectionAsync(repository, ct))?.ConnectionString;

    private static LiveConnectionView View(LiveConnection? c) =>
        c is null ? new(false, null, null, null) : new(true, c.Server, c.Database, c.UpdatedAt, c.Dialect);
}
