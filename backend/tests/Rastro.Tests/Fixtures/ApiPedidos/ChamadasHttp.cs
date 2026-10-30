using System.Net.Http.Json;

namespace Fixture.ApiPedidos;

// Variações de chamada HTTP para os testes da leitura de rota. Ninguém chama esta classe a partir do
// controller: cada teste usa Variacoes como ponto de entrada.
public class ChamadasHttp(HttpClient http, string baseUrl)
{
    private const string Raiz = "api";
    private readonly HttpClient _http = http;
    private readonly string _baseUrl = baseUrl;

    public async Task Variacoes(int id, string livre)
    {
        await _http.GetAsync("api/pedidos/lista");
        await _http.PutAsync($"{Raiz}/pedidos/itens", null);
        await _http.DeleteAsync($"{_baseUrl}/api/pedidos/{id}/itens");
        await _http.GetStringAsync(_baseUrl + "/api/estornos/" + id);
        await _http.PatchAsync("https://pagamentos.exemplo.com:5000/api/estornos/ajuste?forca=1#topo", null);
        await _http.GetFromJsonAsync<string>(livre);
        await _http.SendAsync(new HttpRequestMessage(HttpMethod.Post, "api/estornos"));
        await _http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, livre));
    }

    public async Task Montada(HttpRequestMessage requisicao)
    {
        await _http.SendAsync(requisicao);
    }

    // Sem a opção de chamadas HTTP, vira o nó externo de sempre.
    public async Task Externo()
    {
        await _http.PostAsync("https://gateway.exemplo.com/v1/estornos", null);
    }
}
