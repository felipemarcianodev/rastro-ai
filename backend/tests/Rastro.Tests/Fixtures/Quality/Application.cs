using Fixture.Quality.Domain;

namespace Fixture.Quality.Application;

public interface IA { } public interface IB { } public interface IC { } public interface ID { }
public interface IE { } public interface IF { } public interface IG { }
public interface IAuditoria { void Registrar(string texto); }

// Serviço de aplicação com as "bizarrices" que o Rastro precisa trazer à luz.
public class PedidoAppService(
    IPedidoRepository repository, PedidoDomainService domain, IServiceProvider provider,
    IA a, IB b, IC c, ID d, IE e)
{
    public async Task<bool> AprovarAsync(Guid id)
    {
        var pedido = await repository.ObterAsync(id);
        pedido.Status = "aprovado";
        pedido.Total += 10;
        repository.Atualizar(pedido);
        domain.Cancelar(pedido);

        var auditoria = (IAuditoria?)provider.GetService(typeof(IAuditoria));
        try
        {
            auditoria?.Registrar("aprovado");
        }
        catch
        {
        }

        return Classificar(pedido.Total, pedido.Status) > 0;
    }

    public int Classificar(decimal total, string status)
    {
        try
        {
            if (total > 1000 && status == "aprovado") return 3;
            if (total > 500 || status == "urgente") return 2;
            for (var i = 0; i < 3; i++)
            {
                if (total > i)
                {
                    while (total > 0)
                    {
                        if (status == "x") return 9;
                        total -= 100;
                    }
                }
            }
            return status switch { "a" => 1, "b" => 2, "c" => 3, _ => 0 };
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }
}

public class PedidoRepository : IPedidoRepository
{
    public Task<Pedido> ObterAsync(Guid id) => Task.FromResult(new Pedido());

    // O repositório grava: não é ele quem pula o domínio.
    public void Atualizar(Pedido pedido) => pedido.Status = pedido.Status;
}
