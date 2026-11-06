using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Rastro.Core.Database;

namespace Rastro.Infrastructure.Database.Live;

// A conexão com o banco vivo de um repositório (Fase 40b, ADR-006). A string fica cifrada com o
// DataProtection, num propósito próprio, e só sai daqui para abrir a conexão: nunca volta na resposta,
// nunca vai para o log e nunca vai para a IA. Server e Database ficam à parte, para a tela mostrar.
public class LiveConnection
{
    public string Repository { get; set; } = string.Empty;
    public string ConnectionProtected { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string? Database { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class LiveConnectionStore(RastroDbContext db, IDataProtectionProvider protection, TimeProvider clock)
{
    private const string Purpose = "Rastro.Database.LiveConnection";
    public const int MaxLength = 2000;

    private IDataProtector Protector => protection.CreateProtector(Purpose);

    public async Task<LiveConnectionView> ViewAsync(string repository, CancellationToken ct) =>
        View(await db.LiveConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Repository == repository, ct));

    // ArgumentException: string vazia, longa demais, que o driver não entende ou sem servidor.
    public async Task<LiveConnectionView> SaveAsync(string repository, string? connectionString, CancellationToken ct)
    {
        var text = connectionString?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > MaxLength) throw new ArgumentException("String de conexão vazia ou longa demais.");
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

        var entity = await db.LiveConnections.FirstOrDefaultAsync(c => c.Repository == repository, ct);
        if (entity is null)
        {
            entity = new LiveConnection { Repository = repository };
            db.LiveConnections.Add(entity);
        }
        entity.ConnectionProtected = Protector.Protect(text);
        entity.Server = builder.DataSource;
        entity.Database = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? null : builder.InitialCatalog;
        entity.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return View(entity);
    }

    public async Task RemoveAsync(string repository, CancellationToken ct) =>
        await db.LiveConnections.Where(c => c.Repository == repository).ExecuteDeleteAsync(ct);

    // Nula sem conexão cadastrada.
    public async Task<string?> ConnectionStringAsync(string repository, CancellationToken ct)
    {
        var entity = await db.LiveConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Repository == repository, ct);
        return entity is null ? null : Protector.Unprotect(entity.ConnectionProtected);
    }

    private static LiveConnectionView View(LiveConnection? c) =>
        c is null ? new(false, null, null, null) : new(true, c.Server, c.Database, c.UpdatedAt);
}
