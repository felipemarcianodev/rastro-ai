using Microsoft.AspNetCore.Mvc;

namespace Fixture.NegocioPagamentos;

public record EstornoRequest(int PedidoId, string Meio);

// Fixture da visão de negócio (Fase 55): sistema que atende o estorno e avisa o cliente por e-mail.
[ApiController]
[Route("api/[controller]")]
public class EstornosController(EstornosService estornos) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Criar(EstornoRequest request)
    {
        await estornos.EstornarAsync(request);
        return Ok();
    }
}
