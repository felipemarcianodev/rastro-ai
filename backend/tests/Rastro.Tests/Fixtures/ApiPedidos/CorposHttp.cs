using System.Net.Http.Json;

namespace Fixture.ApiPedidos;

public record ItemPedido(string Sku, int Quantidade);

public record ItemEspecial(string Sku, int Quantidade) : ItemPedido(Sku, Quantidade)
{
    public string Observacao { get; set; } = "";
    public string Escondido { private get; set; } = "";
    public static string Estatica { get; set; } = "";
}

// Corpos de chamada HTTP para os testes dos campos enviados. Ninguém chama esta classe a partir do
// controller: cada teste usa um método dela como entrada.
public class CorposHttp(HttpClient http)
{
    public async Task Tipado(ItemPedido item)
    {
        await http.PostAsJsonAsync("api/itens", item);
    }

    public async Task Derivado(ItemEspecial item)
    {
        await http.PutAsJsonAsync("api/itens", item);
    }

    public async Task Anonimo(int id)
    {
        await http.PatchAsJsonAsync("api/itens", new { id, motivo = "x" });
    }

    public async Task Objeto(object corpo)
    {
        await http.PostAsJsonAsync("api/itens", corpo);
    }

    public async Task Lista(List<ItemPedido> itens)
    {
        await http.PostAsJsonAsync("api/itens", itens);
    }

    public async Task Conteudo(ItemPedido item)
    {
        await http.PostAsync("api/itens", JsonContent.Create(item));
    }

    public async Task Texto()
    {
        await http.PostAsync("api/itens", new StringContent("x"));
    }

    public async Task SemCorpo()
    {
        await http.GetAsync("api/itens");
    }
}
