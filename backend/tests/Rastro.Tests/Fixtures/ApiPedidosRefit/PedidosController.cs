using Microsoft.AspNetCore.Mvc;
using Refit;

namespace Fixture.ApiPedidosRefit;

public record EstornoDto(int PedidoId, string Meio);

// Cliente tipado do Refit: a interface não tem implementação no código (o Refit gera em tempo de execução).
public interface IPagamentosApi
{
    [Post("/api/estornos")]
    Task Criar([Body] EstornoDto estorno);

    [Get("/api/estornos/{id}?detalhe=true")]
    Task<string> Obter(int id);

    [Delete("/api/estornos/{id}")]
    Task Remover(int id);

    [Get("/api/ajuda/{**caminho}")]
    Task<string> Ajuda(string caminho);

    // Sem atributo do Refit: continua sem implementação, como qualquer interface.
    Task Sincronizar();
}

// Atributo Get de outro namespace (não é o do Refit): não vira chamada HTTP.
public interface ILocal
{
    [Outro.Get("/api/local")]
    Task Local();
}

[ApiController]
[Route("api/[controller]")]
public class PedidosController(IPagamentosApi pagamentos, ILocal local) : ControllerBase
{
    [HttpPost("{id}/cancelamento")]
    public async Task Cancelar(int id)
    {
        if (DateTime.UtcNow.Hour >= 18)
            throw new InvalidOperationException("Pedido fora do prazo de cancelamento.");

        await pagamentos.Criar(new EstornoDto(id, "cartao"));
        await pagamentos.Obter(id);
    }

    public async Task Variacoes(int id)
    {
        await pagamentos.Remover(id);
        await pagamentos.Ajuda("a/b");
        await pagamentos.Sincronizar();
        await local.Local();
    }
}
