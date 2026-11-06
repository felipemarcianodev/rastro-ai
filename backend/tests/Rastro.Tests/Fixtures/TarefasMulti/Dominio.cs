namespace Tarefas.Dominio;

// Sistema fictício em dois projetos (Fase 43c): o Domínio tem as interfaces e os serviços; a Infra tem os
// repositórios. O Domínio não enxerga a Infra, como numa solution de camadas.
public class Tarefa
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Titulo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public int Prioridade { get; set; }
}

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id);
    Task<List<T>> GetAllAsync(Guid tenantId);
}

public interface ITarefaRepository : IRepository<Tarefa>
{
    new Task<List<Tarefa>> GetAllAsync(Guid tenantId);
}

public interface ITarefaService
{
    Task<Tarefa?> GetByIdAsync(Guid id);
    Task<Tarefa?> GetByIdComAwaitAsync(Guid id);
    Task<List<Tarefa>> ListAsync(Guid tenantId);
    Task<List<Tarefa>> ListComAwaitAsync(Guid tenantId);
}

// Serviço fino: só repassa a chamada ao repositório, às vezes com await.
public class TarefaService(ITarefaRepository repository) : ITarefaService
{
    private readonly ITarefaRepository _repository = repository;

    public Task<Tarefa?> GetByIdAsync(Guid id) => _repository.GetByIdAsync(id);

    public async Task<Tarefa?> GetByIdComAwaitAsync(Guid id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public Task<List<Tarefa>> ListAsync(Guid tenantId) => _repository.GetAllAsync(tenantId);

    public async Task<List<Tarefa>> ListComAwaitAsync(Guid tenantId) => await _repository.GetAllAsync(tenantId);
}

// Um serviço fino sobre outro serviço fino: dois saltos até o repositório.
public class TarefaFachada(ITarefaService servico)
{
    private readonly ITarefaService _servico = servico;

    public Task<Tarefa?> Obter(Guid id) => _servico.GetByIdAsync(id);

    public Task<List<Tarefa>> Listar(Guid tenantId) => _servico.ListAsync(tenantId);
}

public class GenericoService(IRepository<Tarefa> repository, ITarefaRepository especifico)
{
    // Pela interface base genérica: o "new" da derivada esconde o membro, mas a classe implementa os dois.
    public Task<List<Tarefa>> PelaBase(Guid tenantId) => repository.GetAllAsync(tenantId);

    public Task<List<Tarefa>> PelaDerivada(Guid tenantId) => especifico.GetAllAsync(tenantId);
}

public class BulkService(ITarefaService tarefaService, TarefaFachada fachada)
{
    private readonly ITarefaService _tarefaService = tarefaService;
    private readonly TarefaFachada _fachada = fachada;

    public async Task Lote(List<Guid> ids)
    {
        foreach (var id in ids)
        {
            var x = await _tarefaService.GetByIdAsync(id);
            if (x is null) continue;
        }
    }

    public async Task LoteComAwait(List<Guid> ids)
    {
        foreach (var id in ids)
        {
            var x = await _tarefaService.GetByIdComAwaitAsync(id);
            if (x is null) continue;
        }
    }

    public async Task LotePelaFachada(List<Guid> ids)
    {
        foreach (var id in ids)
            await _fachada.Obter(id);
    }

    public async Task<List<Tarefa>> Filtrar(Guid tenantId, int prioridade)
    {
        var todas = await _tarefaService.ListAsync(tenantId);
        return todas.Where(t => t.Prioridade == prioridade).ToList();
    }

    public async Task<List<Tarefa>> FiltrarComAwait(Guid tenantId, int prioridade)
    {
        var todas = await _tarefaService.ListComAwaitAsync(tenantId);
        return todas.Where(t => t.Prioridade == prioridade).ToList();
    }

    public async Task<List<Tarefa>> FiltrarPelaFachada(Guid tenantId, int prioridade)
    {
        var todas = await _fachada.Listar(tenantId);
        return todas.Where(t => t.Prioridade == prioridade).ToList();
    }

    // Sem laço e sem filtro: a chamada ao serviço fino não é achado.
    public Task<List<Tarefa>> Todas(Guid tenantId) => _tarefaService.ListAsync(tenantId);
}

// Recursão entre dois métodos de serviço: a cadeia não pode girar sem fim.
public class CicloService(ITarefaService servico)
{
    private readonly ITarefaService _servico = servico;

    public async Task Lote(List<Guid> ids)
    {
        foreach (var id in ids)
            await Ping(id, 3);
    }

    private Task<Tarefa?> Ping(Guid id, int n) => n <= 0 ? _servico.GetByIdAsync(id) : Pong(id, n - 1);

    private Task<Tarefa?> Pong(Guid id, int n) => Ping(id, n);
}

// Cadeias de serviços finos, para o limite de saltos: a de B chega ao repositório no quarto salto, a de C no quinto.
public class CadeiaB(ITarefaRepository repositorio)
{
    private readonly ITarefaRepository _repositorio = repositorio;

    public Task<Tarefa?> B1(Guid id) => B2(id);
    private Task<Tarefa?> B2(Guid id) => B3(id);
    private Task<Tarefa?> B3(Guid id) => B4(id);
    private Task<Tarefa?> B4(Guid id) => _repositorio.GetByIdAsync(id);
}

public class CadeiaC(ITarefaRepository repositorio)
{
    private readonly ITarefaRepository _repositorio = repositorio;

    public Task<Tarefa?> C1(Guid id) => C2(id);
    private Task<Tarefa?> C2(Guid id) => C3(id);
    private Task<Tarefa?> C3(Guid id) => C4(id);
    private Task<Tarefa?> C4(Guid id) => C5(id);
    private Task<Tarefa?> C5(Guid id) => _repositorio.GetByIdAsync(id);
}

public class CadeiasService(CadeiaB b, CadeiaC c)
{
    public async Task LoteB(List<Guid> ids)
    {
        foreach (var id in ids) await b.B1(id);
    }

    public async Task LoteC(List<Guid> ids)
    {
        foreach (var id in ids) await c.C1(id);
    }
}

// Forma do sistema real: a interface derivada NÃO redeclara o membro; a classe concreta o reimplementa com "new".
public interface IBaseRepo<T> where T : class
{
    Task<List<T>> GetAllAsync(string tenantId);
}

public interface IPaiRepo : IBaseRepo<Tarefa>
{
    Task<List<Tarefa>> ListByPaiAsync(Guid paiId);
}

public class PaiService(IPaiRepo repository, IBaseRepo<Tarefa> baseRepository)
{
    private readonly IPaiRepo _repository = repository;

    public async Task<List<Tarefa>> PeloReceptorDerivado(string tenantId) => await _repository.GetAllAsync(tenantId);

    public Task<List<Tarefa>> PelaBaseDireto(string tenantId) => baseRepository.GetAllAsync(tenantId);
}
