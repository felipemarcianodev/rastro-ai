using Microsoft.AspNetCore.Mvc;

namespace Fixture.ApiPagamentos;

public record EstornoRequest(int PedidoId, string Meio);

// Sistema que atende: estorno no cartão passa pelo gateway externo; no boleto, grava no repositório.
[ApiController]
[Route("api/[controller]")]
public class EstornosController(GatewayCartao gateway, EstornoRepository repositorio) : ControllerBase
{
    [HttpPost]
    public async Task Criar(EstornoRequest request)
    {
        if (request.Meio == "cartao")
            await gateway.EstornarAsync(request.PedidoId);
        else if (request.Meio == "boleto")
            await repositorio.GravarAsync(request.PedidoId);
        else
            throw new ArgumentException("Meio de pagamento desconhecido.");
    }

    [HttpGet("{id:int}")]
    public Task<string> Obter(int id) => repositorio.BuscarAsync(id);
}

public class GatewayCartao(HttpClient http)
{
    private readonly HttpClient _http = http;

    public async Task EstornarAsync(int pedidoId)
    {
        // Host de fora: nenhum repositório cadastrado atende esta rota.
        await _http.PostAsync("https://gateway.exemplo.com/v1/estornos", new StringContent(pedidoId.ToString()));
    }
}

public class EstornoRepository
{
    private readonly List<int> _gravados = [];

    public Task GravarAsync(int pedidoId)
    {
        _gravados.Add(pedidoId);
        return Task.CompletedTask;
    }

    public Task<string> BuscarAsync(int id) => Task.FromResult(_gravados.Contains(id) ? "estornado" : "pendente");
}
