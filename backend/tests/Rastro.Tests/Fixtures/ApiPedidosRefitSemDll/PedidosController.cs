using Microsoft.AspNetCore.Mvc;
using Refit;

namespace Fixture.ApiPedidosRefitSemDll;

// Esta fixture NÃO declara o namespace Refit (como um projeto cuja dll não foi resolvida, e por isso
// não compila): o atributo fica sem símbolo e só o "using Refit" revela de onde ele vem.
public interface IPagamentosApi
{
    [Post("/api/estornos")]
    Task Criar(EstornoDto estorno);
}

public record EstornoDto(int PedidoId, string Meio);

[ApiController]
[Route("api/[controller]")]
public class PedidosController(IPagamentosApi pagamentos) : ControllerBase
{
    [HttpPost("{id}/cancelamento")]
    public async Task Cancelar(int id)
    {
        await pagamentos.Criar(new EstornoDto(id, "cartao"));
    }
}
