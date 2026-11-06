using Microsoft.EntityFrameworkCore;
using Tarefas.Dominio;

namespace Tarefas.Infra;

// Fase 43d: repositórios e gravações da fixture fictícia (ver Fluxo43d.cs no Domínio).
public class ListagemRepository(AppDbContext db) : IListagemRepository
{
    public async Task<List<Item>> PorEspacoAsync(Guid tenantId, Guid espacoId) =>
        await db.Itens.AsNoTracking().Where(x => x.TenantId == tenantId && x.EspacoId == espacoId).ToListAsync();

    public async Task<List<Item>> PorTipoAsync(Guid tenantId, Guid tipoId) =>
        await db.Itens.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync();

    public async Task<List<Item>> TodosAsync(Guid tenantId) =>
        await db.Itens.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync();
}

public class EspacoRepository(AppDbContext db) : IEspacoRepository
{
    public async Task<List<Espaco>> BuscarPorNomeAsync(Guid tenantId, string termo, int limite)
    {
        return await db.Espacos
            .Where(x => x.TenantId == tenantId && x.Nome.Contains(termo))
            .Take(limite)
            .ToListAsync();
    }

    public async Task<List<Espaco>> BuscarSemLimiteAsync(Guid tenantId, string termo) =>
        await db.Espacos.Where(x => x.TenantId == tenantId && x.Nome.Contains(termo)).ToListAsync();

    public async Task<List<Espaco>> PaginaAsync(Guid tenantId, string termo, int pagina) =>
        await db.Espacos.Where(x => x.TenantId == tenantId && x.Nome.Contains(termo)).Skip(pagina * 10).Take(10).ToListAsync();

    public async Task<List<Espaco>> LimiteSemWhereAsync(int limite) =>
        await db.Espacos.Take(limite).ToListAsync();
}

// SQL cru que grava, com o texto interpolado e verbatim, como num serviço de sincronização real.
public class ReferenciaService(AppDbContext db) : IReferenciaService
{
    public async Task RemoverReferenciasAsync(Guid tenantId, Guid itemId)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE itens
            SET referencias = '[]'
            WHERE tenant_id = {tenantId}
            AND referencias @> {itemId}");
    }
}

public class DiarioRepository(AppDbContext db) : IDiarioRepository
{
    public Task<int> LimparReferenciaAsync(Guid tenantId, Guid itemId)
        => db.Diarios.Where(x => x.TenantId == tenantId && x.ItemId == itemId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ItemId, (Guid?)null));
}

public class ContadorRepository(AppDbContext db) : IContadorRepository
{
    public async Task IncrementarContadorAsync(Guid tenantId, Guid espacoId, int incremento)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE espacos SET contador = contador + {incremento} WHERE tenant_id = {tenantId} AND id = {espacoId}");
    }

    public Task<int> ApagarComentariosAsync(Guid itemId) => Task.FromResult(0);

    public Task AtualizarRastreadoAsync(Item item)
    {
        db.Itens.Update(item);
        return Task.CompletedTask;
    }
}

// Repositório genérico com ExecuteDelete: o T vem fechado pela interface da chamada (IApagador<Comentario>).
public class EfApagador<T>(AppDbContext db) : IApagador<T> where T : class
{
    private readonly DbSet<T> _set = db.Set<T>();

    public Task<int> ApagarAsync(Func<T, bool> filtro) => _set.Where(filtro).ExecuteDeleteAsync();
}
