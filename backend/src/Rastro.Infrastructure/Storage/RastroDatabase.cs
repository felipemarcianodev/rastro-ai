using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;

namespace Rastro.Infrastructure.Storage;

// Banco SQLite (ADR-002). Sem ConnectionStrings:Rastro, o arquivo fica na pasta do usuário.
public static class RastroDatabase
{
    public static string ConnectionString(IConfiguration configuration)
    {
        if (configuration.GetConnectionString("Rastro") is { Length: > 0 } configured) return configured;

        RastroHome.EnsureFolder();
        return new SqliteConnectionStringBuilder { DataSource = RastroHome.DatabasePath }.ToString();
    }

    // O SQLite não ordena nem compara DateTimeOffset: vira número (ticks UTC + offset), que ordena igual.
    public static void UseSortableDates(ModelConfigurationBuilder configuration) =>
        configuration.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();

    // WAL: a API e o servidor MCP abrem o mesmo arquivo; leitura não espera escrita. Fica gravado no arquivo.
    public static Task UseWriteAheadLogAsync(DbContext db, CancellationToken ct = default) =>
        db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
}
