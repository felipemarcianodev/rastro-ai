using Microsoft.EntityFrameworkCore;
using Tarefas.Dominio;

namespace Tarefas.Infra;

public class AppDbContext : DbContext
{
    public DbSet<Tarefa> Tarefas => Set<Tarefa>();
    public DbSet<Item> Itens => Set<Item>();
    public DbSet<Espaco> Espacos => Set<Espaco>();
    public DbSet<Diario> Diarios => Set<Diario>();
}

public class EfRepository<T> : IRepository<T> where T : class
{
    protected readonly AppDbContext _db;
    protected readonly DbSet<T> _set;

    public EfRepository(AppDbContext db)
    {
        _db = db;
        _set = db.Set<T>();
    }

    public virtual Task<T?> GetByIdAsync(Guid id) => _set.AsNoTracking().FirstOrDefaultAsync(e => e != null);

    public virtual async Task<List<T>> GetAllAsync(Guid tenantId) => await _set.AsNoTracking().ToListAsync();
}

public class TarefaRepository : EfRepository<Tarefa>, ITarefaRepository
{
    public TarefaRepository(AppDbContext db) : base(db) { }

    public new async Task<List<Tarefa>> GetAllAsync(Guid tenantId) =>
        await _db.Tarefas.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync();
}

// Busca por trecho de texto: o índice de trigrama da coluna Titulo, se o esquema tiver, atende o curinga.
public class BuscaRepository(AppDbContext db)
{
    public async Task<List<Tarefa>> Buscar(string termo)
    {
        var padrao = $"%{termo}%";
        return await db.Tarefas.AsNoTracking().Where(t => EF.Functions.ILike(t.Titulo, padrao)).ToListAsync();
    }

    public async Task<List<Tarefa>> BuscarNoMeio(string termo) =>
        await db.Tarefas.AsNoTracking().Where(t => t.Titulo.ToLower().Contains("%" + termo)).ToListAsync();

    public async Task<List<Tarefa>> BuscarOutraColuna(string termo) =>
        await db.Tarefas.AsNoTracking().Where(t => EF.Functions.ILike(t.Descricao, $"%{termo}%")).ToListAsync();
}

public class BaseRepo<T> : IBaseRepo<T> where T : class
{
    protected readonly AppDbContext _db;

    public BaseRepo(AppDbContext db) => _db = db;

    public async Task<List<T>> GetAllAsync(string tenantId) => await _db.Set<T>().AsNoTracking().ToListAsync();
}

public class PaiRepo : BaseRepo<Tarefa>, IPaiRepo
{
    public PaiRepo(AppDbContext db) : base(db) { }

    public new async Task<List<Tarefa>> GetAllAsync(string tenantId) =>
        await _db.Tarefas.AsNoTracking().Where(x => x.Titulo == tenantId).ToListAsync();

    public Task<List<Tarefa>> ListByPaiAsync(Guid paiId) => Task.FromResult(new List<Tarefa>());
}
