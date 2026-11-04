using System.Threading;
using Refit;

namespace Fixture.ApiPedidosRefit;

public record ItemDto(string Sku, int Quantidade);

// Cada tipo de parâmetro do Refit: rota, corpo, query, cabeçalho e cancelamento.
public interface ICamposApi
{
    [Post("/api/itens/{pedidoId}")]
    Task Adicionar(int pedidoId, [Body] ItemDto item, [Query] string moeda, [Header("X-Trace")] string trace, CancellationToken ct);

    [Post("/api/itens/lote")]
    Task Lote([Body] List<ItemDto> itens);

    [Get("/api/itens/busca")]
    Task Buscar([Query] string termo, int pagina);
}

// Ninguém chama esta classe a partir do controller: cada teste usa um método dela como entrada.
public class CamposEntrada(ICamposApi api)
{
    public async Task Adicionar(CancellationToken ct)
    {
        await api.Adicionar(1, new ItemDto("a", 1), "BRL", "t", ct);
    }

    public async Task Lote()
    {
        await api.Lote([new ItemDto("a", 1)]);
    }

    public async Task Buscar()
    {
        await api.Buscar("x", 2);
    }
}
