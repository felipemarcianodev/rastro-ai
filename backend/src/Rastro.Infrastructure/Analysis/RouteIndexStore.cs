using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rastro.Core.Http;
using Rastro.Core.Surveys;
using Rastro.Extractor;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Infrastructure.Analysis;

// Índice de rotas de um repositório (Fase 25), guardado por commit: a costura não precisa reler todos
// os .cs do outro sistema a cada levantamento nem abrir a solution dele só para achar a rota.
// Sem git (commit desconhecido) monta o índice na hora e não guarda, porque não há como saber
// quando ele envelheceu.
public class RouteIndexStore(
    RastroDbContext db,
    RepositoryCatalog catalog,
    IGitInfo git,
    TimeProvider clock,
    ILogger<RouteIndexStore> logger)
{
    public async Task<IReadOnlyList<RouteEntry>> GetAsync(string repository, CancellationToken ct)
    {
        var root = catalog.RootOf(repository);
        if (root is null)
        {
            logger.LogWarning("Índice de rotas pedido para repositório fora da configuração: {Repository}.", repository);
            return [];
        }

        var head = await git.HeadAsync(root, ct);
        if (head is not null)
        {
            var stored = await db.RouteIndexes.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Repository == repository && i.CommitHash == head.Commit, ct);
            if (stored is not null) return stored.ReadEntries();
        }

        // A leitura dos .cs é só sintaxe, mas é E/S de muitos arquivos: fora da thread da requisição.
        var entries = await Task.Run(() => RouteIndexBuilder.Build(root), ct);
        logger.LogInformation("Índice de rotas de {Repository} montado: {RouteCount} rota(s), {Stored}.",
            repository, entries.Count, head is null ? "sem git, não guardado" : "guardado por commit");
        if (head is null) return entries;

        var row = RouteIndex.Create(repository, head.Commit, entries, clock.GetUtcNow());
        db.RouteIndexes.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Outra requisição guardou o mesmo commit primeiro: o índice montado aqui continua válido.
            logger.LogWarning(ex, "Índice de rotas de {Repository} não foi guardado (já existe para o commit).", repository);
            db.Entry(row).State = EntityState.Detached;
        }
        return entries;
    }
}
