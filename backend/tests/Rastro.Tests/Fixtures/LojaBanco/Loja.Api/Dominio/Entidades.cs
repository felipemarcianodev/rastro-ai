using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Loja.Api.Dominio;

[Index(nameof(Email), IsUnique = true, Name = "UX_clientes_email")]
public class Cliente
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Nome { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    [Column(TypeName = "char(11)")]
    public string? Cpf { get; set; }

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
    public ICollection<ItemPedido> Itens { get; } = [];
    public ICollection<Pagamento> Pagamentos { get; } = [];

    [NotMapped]
    public bool Pago => Pagamentos.Any(p => p.PagoEm is not null);
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
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;
    public MetodoPagamento Metodo { get; set; }

    [Precision(10, 2)]
    public decimal Valor { get; set; }

    public DateTime? PagoEm { get; set; }
}

public enum MetodoPagamento : byte
{
    Pix = 0,
    Cartao = 1,
    Boleto = 2
}
