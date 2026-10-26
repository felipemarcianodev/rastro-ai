namespace Fixture.Quality.Domain;

public class Pedido
{
    public Guid Id { get; private set; }
    public string Status { get; set; } = "aberto";
    public decimal Total { get; set; }
    public string Cliente { get; private set; } = "";

    public void Aprovar() => Status = "aprovado";
}

public interface IPedidoRepository
{
    Task<Pedido> ObterAsync(Guid id);
    void Atualizar(Pedido pedido);
}

// Serviço de domínio: alterar a entidade aqui é o lugar certo.
public class PedidoDomainService(IPedidoRepository repository)
{
    public void Cancelar(Pedido pedido)
    {
        pedido.Status = "cancelado";
        repository.Atualizar(pedido);
    }
}
