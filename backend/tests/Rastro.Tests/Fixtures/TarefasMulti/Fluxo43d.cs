namespace Tarefas.Dominio;

// Fase 43d: a forma de um sistema real com nomes fictícios. A lista vem do repositório por um serviço fino, fica
// numa variável (atribuída em ramos diferentes) e o filtro em memória vem depois; a gravação por ExecuteUpdate,
// ExecuteDelete e SQL cru acontece dentro de um laço, direto ou por uma cadeia de serviços.
public class Item
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EspacoId { get; set; }
    public Guid? DiarioId { get; set; }
    public string Nome { get; set; } = "";
    public List<string> Tags { get; set; } = [];
}

public class Espaco
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Nome { get; set; } = "";
}

public class Comentario
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
}

public class Diario
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ItemId { get; set; }
}

public interface IListagemRepository
{
    Task<List<Item>> PorEspacoAsync(Guid tenantId, Guid espacoId);
    Task<List<Item>> PorTipoAsync(Guid tenantId, Guid tipoId);
    Task<List<Item>> TodosAsync(Guid tenantId);
}

public interface IListagemService
{
    Task<List<Item>> PorEspacoAsync(Guid tenantId, Guid espacoId);
    Task<List<Item>> PorTipoAsync(Guid tenantId, Guid tipoId);
    Task<List<Item>> TodosAsync(Guid tenantId);
}

// Serviço fino: só repassa ao repositório.
public class ListagemService(IListagemRepository repositorio) : IListagemService
{
    public Task<List<Item>> PorEspacoAsync(Guid tenantId, Guid espacoId) => repositorio.PorEspacoAsync(tenantId, espacoId);

    public async Task<List<Item>> PorTipoAsync(Guid tenantId, Guid tipoId) => await repositorio.PorTipoAsync(tenantId, tipoId);

    public Task<List<Item>> TodosAsync(Guid tenantId) => repositorio.TodosAsync(tenantId);
}

public class FiltroService(IListagemService listagem)
{
    // A forma real: variável declarada, atribuída em if/else, e o Where em memória por reatribuição, encadeado.
    public async Task<List<Item>> Filtrar(Guid tenantId, Guid? espacoId, Guid? tipoId, HashSet<Guid>? acessiveis, string? tag)
    {
        List<Item> itens;
        if (espacoId.HasValue)
            itens = await listagem.PorEspacoAsync(tenantId, espacoId.Value);
        else if (tipoId.HasValue)
            itens = await listagem.PorTipoAsync(tenantId, tipoId.Value);
        else
            itens = await listagem.TodosAsync(tenantId);

        if (acessiveis is not null)
            itens = itens.Where(i => acessiveis.Contains(i.EspacoId)).ToList();

        if (!string.IsNullOrEmpty(tag))
            itens = itens.Where(i => i.Tags.Contains(tag)).Where(i => i.Nome.Length > 0).ToList();

        return itens;
    }

    // A variável nasce de um condicional (?:) com await nos dois ramos.
    public async Task<List<Item>> ComCondicional(Guid tenantId, Guid? tipoId, HashSet<Guid> acessiveis)
    {
        var itens = tipoId.HasValue
            ? await listagem.PorTipoAsync(tenantId, tipoId.Value)
            : await listagem.TodosAsync(tenantId);

        itens = itens.OrderBy(i => i.Nome).ToList();
        return itens.Where(i => acessiveis.Contains(i.EspacoId)).ToList();
    }

    // Só a contagem da lista (propriedade), sem filtro: não é achado.
    public async Task<int> SoContagem(Guid tenantId)
    {
        var itens = await listagem.TodosAsync(tenantId);
        return itens.Count;
    }

    // A lista é devolvida como veio: não é achado.
    public async Task<List<Item>> SemFiltro(Guid tenantId)
    {
        List<Item> itens;
        itens = await listagem.TodosAsync(tenantId);
        return itens;
    }
}

public interface IEspacoRepository
{
    Task<List<Espaco>> BuscarPorNomeAsync(Guid tenantId, string termo, int limite);
    Task<List<Espaco>> BuscarSemLimiteAsync(Guid tenantId, string termo);
    Task<List<Espaco>> PaginaAsync(Guid tenantId, string termo, int pagina);
    Task<List<Espaco>> LimiteSemWhereAsync(int limite);
}

public class BuscaGlobalService(IEspacoRepository espacos)
{
    // O repositório já filtra e limita no banco: filtrar por acesso depois, sobre poucas linhas, não é achado.
    public async Task<List<Espaco>> Buscar(Guid tenantId, string termo, HashSet<Guid> acessiveis)
    {
        var encontrados = await espacos.BuscarPorNomeAsync(tenantId, termo, 5);
        encontrados = encontrados.Where(e => acessiveis.Contains(e.Id)).ToList();
        return encontrados;
    }

    public async Task<List<Espaco>> BuscarPaginado(Guid tenantId, string termo, HashSet<Guid> acessiveis)
    {
        var encontrados = await espacos.PaginaAsync(tenantId, termo, 2);
        return encontrados.Where(e => acessiveis.Contains(e.Id)).ToList();
    }

    // Filtra no banco mas não limita: o filtro em memória ainda roda sobre muitas linhas.
    public async Task<List<Espaco>> BuscarSemLimite(Guid tenantId, string termo, HashSet<Guid> acessiveis)
    {
        var encontrados = await espacos.BuscarSemLimiteAsync(tenantId, termo);
        return encontrados.Where(e => acessiveis.Contains(e.Id)).ToList();
    }

    // Limita mas não filtra no banco: continua achado.
    public async Task<List<Espaco>> LimiteSemWhere(HashSet<Guid> acessiveis)
    {
        var encontrados = await espacos.LimiteSemWhereAsync(10);
        return encontrados.Where(e => acessiveis.Contains(e.Id)).ToList();
    }
}

public interface IReferenciaService
{
    Task RemoverReferenciasAsync(Guid tenantId, Guid itemId);
}

public interface IDiarioRepository
{
    Task<int> LimparReferenciaAsync(Guid tenantId, Guid itemId);
}

public interface IContadorRepository
{
    Task IncrementarContadorAsync(Guid tenantId, Guid espacoId, int incremento);
    Task<int> ApagarComentariosAsync(Guid itemId);
    Task AtualizarRastreadoAsync(Item item);
}

public interface IApagador<T> where T : class
{
    Task<int> ApagarAsync(Func<T, bool> filtro);
}

// Serviço fino sobre o contador: um salto até o repositório.
public class ContadorService(IContadorRepository repositorio)
{
    public Task Incrementar(Guid tenantId, Guid espacoId, int incremento) => repositorio.IncrementarContadorAsync(tenantId, espacoId, incremento);
}

public class LoteService(
    IReferenciaService referencias,
    IDiarioRepository diario,
    ContadorService contador,
    IContadorRepository contadores,
    IApagador<Comentario> comentarios)
{
    // A forma real do BulkDelete: cada volta faz três gravações que vão ao banco na hora.
    public async Task Apagar(Guid tenantId, List<Guid> ids, Guid espacoId)
    {
        foreach (var id in ids)
        {
            await referencias.RemoverReferenciasAsync(tenantId, id);
            await diario.LimparReferenciaAsync(tenantId, id);
            await contador.Incrementar(tenantId, espacoId, -1);
        }
    }

    // ExecuteDelete num repositório genérico, pela interface com o T fechado.
    public async Task ApagarComentarios(List<Guid> ids)
    {
        foreach (var id in ids)
            await comentarios.ApagarAsync(c => c.ItemId == id);
    }

    // Só altera entidade rastreada (o SaveChanges grava de uma vez): não é N+1 de escrita.
    public async Task AtualizarRastreados(List<Item> itens)
    {
        foreach (var item in itens)
            await contadores.AtualizarRastreadoAsync(item);
    }

    // Sem laço: uma gravação só não é achado.
    public async Task ApagarUm(Guid tenantId, Guid id)
    {
        await referencias.RemoverReferenciasAsync(tenantId, id);
        await diario.LimparReferenciaAsync(tenantId, id);
    }

    // A cadeia chega a um método que grava, mas o laço está só na leitura da lista de ids.
    public async Task LacoSoComIds(Guid tenantId, List<Guid> ids)
    {
        var total = 0;
        foreach (var id in ids) total += id.GetHashCode();
        await diario.LimparReferenciaAsync(tenantId, ids.FirstOrDefault());
    }
}
