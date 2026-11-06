using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Loja.Api.Dominio;

[Index(nameof(Email), IsUnique = true, Name = "ux_clientes_email")]
public class Cliente
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Nome { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    [Column(TypeName = "char(11)")]
    public string? Cpf { get; set; }

    public Guid Codigo { get; set; }

    public JsonDocument? Preferencias { get; set; }

    public DateTime CriadoEm { get; set; }

    public ICollection<Pedido> Pedidos { get; } = [];
}

public class Pedido
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime CriadoEm { get; set; }
    public string[]? Etiquetas { get; set; }
    public ICollection<ItemPedido> Itens { get; } = [];
    public ICollection<Pagamento> Pagamentos { get; } = [];
}

public class ItemPedido
{
    public int PedidoId { get; set; }
    public int ProdutoId { get; set; }
    public int Quantidade { get; set; }
    public decimal PrecoUnit { get; set; }
    public Pedido Pedido { get; set; } = null!;
}

public class Pagamento
{
    public long Id { get; set; }
    public int PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;
    public MetodoPagamento Metodo { get; set; }

    [Precision(10, 2)]
    public decimal Valor { get; set; }

    public DateTime? PagoEm { get; set; }
}

public enum MetodoPagamento : short
{
    Pix = 0,
    Cartao = 1,
    Boleto = 2
}
