using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace Fixture.ApiPedidos;

// Sistema que chama: cancelar um pedido fora do prazo é recusado; dentro dele, pede o estorno
// à API de pagamentos por HTTP (fixture ApiPagamentos).
[ApiController]
[Route("api/[controller]")]
public class PedidosController(PagamentosClient pagamentos) : ControllerBase
{
    [HttpPost("{id}/cancelamento")]
    public async Task Cancelar(int id)
    {
        if (DateTime.UtcNow.Hour >= 18)
            throw new InvalidOperationException("Pedido fora do prazo de cancelamento.");

        await pagamentos.EstornarAsync(id);
        await pagamentos.ConsultarAsync("api/estornos/" + id);
    }
}

public class PagamentosClient(HttpClient http)
{
    private readonly HttpClient _http = http;

    public async Task EstornarAsync(int pedidoId)
    {
        await _http.PostAsJsonAsync("api/estornos", new { pedidoId });
    }

    // A rota chega de fora (variável): o extrator não consegue ler.
    public async Task<string> ConsultarAsync(string caminho)
    {
        return await _http.GetStringAsync(caminho);
    }
}
