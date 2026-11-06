using Microsoft.EntityFrameworkCore;

namespace Loja.Fluxo;

public class PedidoRepositorio(LojaContext db)
{
    // Só lê, sem AsNoTracking, e filtra depois de trazer a tabela inteira.
    public async Task<List<Pedido>> AbertosAsync()
    {
        var todos = await db.Pedidos.ToListAsync();
        return todos.Where(p => p.Status == "aberto").ToList();
    }

    // Lê com AsNoTracking: não é achado.
    public Task<List<ItemPedido>> ItensAsync(int pedidoId) =>
        db.ItensPedido.AsNoTracking().Where(i => i.PedidoId == pedidoId).ToListAsync();
}

public class PedidosController(LojaContext db, PedidoRepositorio repositorio)
{
    public async Task<int> Listar()
    {
        var total = 0;
        foreach (var pedido in await repositorio.AbertosAsync())
        {
            var itens = await repositorio.ItensAsync(pedido.Id);
            var pago = await db.Pagamentos.AsNoTracking().FirstOrDefaultAsync(p => p.PedidoId == pedido.Id);
            total += itens.Count + (pago is null ? 0 : 1);
        }
        return total;
    }

    public async Task Fechar(Pedido pedido, ItemPedido removido)
    {
        db.Pedidos.Add(pedido);
        db.Remove(removido);
        db.Database.ExecuteSqlRaw("EXEC dbo.sp_baixar_estoque @p0", pedido.Id);
        var cliente = db.Clientes.FromSqlRaw("EXEC [vendas].[sp_cliente] {0}", pedido.ClienteId).ToList();
        await db.SaveChangesAsync();
    }
}

public class RelatorioController(LojaContext db)
{
    // SQL cru com SELECT: o passo leva o texto para a análise de consulta (Fase 40).
    public List<Pedido> DoAno(int ano) =>
        db.Pedidos.FromSqlRaw("SELECT * FROM pedidos WHERE YEAR(criado_em) = {0} ORDER BY criado_em DESC", ano).ToList();
}
