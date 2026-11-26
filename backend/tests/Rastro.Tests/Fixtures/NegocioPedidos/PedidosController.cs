using Microsoft.AspNetCore.Mvc;

namespace Fixture.NegocioPedidos;

// Fixture da visão de negócio (Fase 55): sistema do meio. Fora do prazo, o controller recusa com BadRequest;
// dentro dele, o serviço pede o estorno à API de pagamentos, grava o cancelamento e o controller devolve Ok.
[ApiController]
[Route("api/[controller]")]
public class PedidosController(CancelamentoService cancelamento) : ControllerBase
{
    [HttpPost("{id}/cancelamento")]
    public async Task<IActionResult> Cancelar(int id)
    {
        if (!cancelamento.DentroDoPrazo(id))
            return BadRequest("Fora do prazo de cancelamento.");

        await cancelamento.CancelarAsync(id);
        return Ok();
    }
}
