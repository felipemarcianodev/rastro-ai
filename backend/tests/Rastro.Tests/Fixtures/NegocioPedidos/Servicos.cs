using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Fixture.NegocioPedidos;

public class CancelamentoService(PagamentosClient pagamentos, PedidosContexto db)
{
    private static readonly TimeSpan PrazoMaximo = TimeSpan.FromDays(7);

    public bool DentroDoPrazo(int pedidoId) =>
        DateTime.UtcNow - DataDoPedido(pedidoId) <= PrazoMaximo;

    public async Task CancelarAsync(int pedidoId)
    {
        await pagamentos.EstornarAsync(pedidoId);
        db.Add(new Cancelamento(pedidoId, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private static DateTime DataDoPedido(int pedidoId) => DateTime.UtcNow.AddDays(-pedidoId);
}

public class PagamentosClient(HttpClient http)
{
    private readonly HttpClient _http = http;

    public async Task EstornarAsync(int pedidoId)
    {
        await _http.PostAsJsonAsync("api/estornos", new { pedidoId });
    }
}

public record Cancelamento(int PedidoId, DateTime Em);

public class PedidosContexto : DbContext
{
    public DbSet<Cancelamento> Cancelamentos { get; } = new();
}
