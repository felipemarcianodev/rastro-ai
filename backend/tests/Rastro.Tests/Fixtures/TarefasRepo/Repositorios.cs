using Microsoft.EntityFrameworkCore;

namespace Tarefas.Repo;

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id);
    Task<List<T>> GetAllAsync(Guid tenantId);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
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

    public virtual async Task<List<T>> GetAllAsync(Guid tenantId) => await _set.Where(e => e != null).ToListAsync();

    public async Task UpdateAsync(T entity)
    {
        var existente = await _set.FindAsync(entity);
        _db.Entry(existente!).CurrentValues.SetValues(entity);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(T entity)
    {
        _set.Remove(entity);
        await _db.SaveChangesAsync();
    }
}

public interface ITarefaRepository : IRepository<Tarefa>
{
    new Task<List<Tarefa>> GetAllAsync(Guid tenantId);
    Task<int> DeleteByPaiAsync(Guid paiId);
}

public class TarefaRepository : EfRepository<Tarefa>, ITarefaRepository
{
    public TarefaRepository(AppDbContext db) : base(db) { }

    public new async Task<List<Tarefa>> GetAllAsync(Guid tenantId) =>
        await _db.Tarefas.Where(x => x.TenantId == tenantId && x.ArquivadaEm == null).ToListAsync();

    public async Task<int> DeleteByPaiAsync(Guid paiId)
    {
        var lista = await _db.Subtarefas.Where(s => s.TarefaId == paiId).ToListAsync();
        _db.Subtarefas.RemoveRange(lista);
        await _db.SaveChangesAsync();
        return lista.Count;
    }
}

public class ClienteRepository : EfRepository<Cliente>
{
    public ClienteRepository(AppDbContext db) : base(db) { }
}
