using Microsoft.EntityFrameworkCore;

namespace Tarefas.Repo;

public class TarefaService(ITarefaRepository tarefas, IRepository<Cliente> clientes, EfRepository<Cliente> clientesDiretos, AppDbContext db)
{
    // O repositório devolve a lista materializada e o serviço filtra na memória.
    public async Task<List<Tarefa>> Filtrar(Guid tenantId, int prioridade)
    {
        var todas = await tarefas.GetAllAsync(tenantId);
        return todas.Where(x => x.Prioridade == prioridade).ToList();
    }

    // Uma ida ao banco a cada volta: o GetByIdAsync do genérico e o UpdateAsync.
    public async Task Lote(List<Guid> ids)
    {
        foreach (var id in ids)
        {
            var tarefa = await tarefas.GetByIdAsync(id);
            await tarefas.UpdateAsync(tarefa!);
        }
    }

    public async Task<List<Tarefa>> Buscar(string termo)
    {
        var padrao = $"%{termo}%";
        return await db.Tarefas.AsNoTracking().Where(t => EF.Functions.ILike(t.Titulo, padrao)).ToListAsync();
    }

    public async Task<List<Tarefa>> BuscarConcatenado(string termo) =>
        await db.Tarefas.AsNoTracking().Where(t => EF.Functions.Like(t.Titulo, "%" + termo + "%")).ToListAsync();

    // Prefixo: o índice atende, não é achado.
    public async Task<List<Tarefa>> BuscarPrefixo(string termo) =>
        await db.Tarefas.AsNoTracking().Where(t => EF.Functions.ILike(t.Titulo, $"{termo}%")).ToListAsync();

    public Task<int> ApagarSubtarefas(Guid paiId) => tarefas.DeleteByPaiAsync(paiId);

    public Task Remover(Tarefa tarefa) => tarefas.DeleteAsync(tarefa);

    public Task<Cliente?> ClientePelaInterface(Guid id) => clientes.GetByIdAsync(id);

    public Task<Cliente?> ClienteDireto(Guid id) => clientesDiretos.GetByIdAsync(id);

    public async Task<List<Tarefa>> Arquivadas(Guid tenantId) => await tarefas.GetAllAsync(tenantId);
}
