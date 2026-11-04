using Rastro.Core.Graph;
using Rastro.Extractor;

namespace Rastro.Tests.Extractor;

// Cenários EXT-FIELDS-NNN: os campos que a chamada entre sistemas envia vão na HttpCall (Fase 34).
public class HttpFieldsExtractionTests
{
    private static async Task<HttpCall> CallAsync(string fixture, string entry)
    {
        var solution = FixtureWorkspace.Load(fixture);
        await FixtureWorkspace.AssertCompilesAsync(solution);
        var result = await FlowExtractor.ExtractWithCallsAsync(solution, entry, new ExtractionOptions
        {
            HttpCalls = true,
            BaseDirectory = FixtureWorkspace.FixturesRoot
        });
        var site = Assert.Single(result.HttpCalls);
        // A aresta e a lista de chamadas carregam a mesma HttpCall.
        Assert.Equal(site.Call, Assert.Single(result.Graph.Edges, e => e.To == site.NodeId).Http);
        return site.Call;
    }

    [Fact] // EXT-FIELDS-001
    public async Task Extrair_PostAsJsonDeRecord_CamposSaoAsPropriedadesDoTipo()
    {
        var call = await CallAsync("ApiPedidos", "CorposHttp.Tipado");

        Assert.Equal(["Sku", "Quantidade"], call.Fields);
        Assert.False(call.MoreFields);
    }

    [Fact] // EXT-FIELDS-002
    public async Task Extrair_TipoDerivado_BaseAntesDaDerivadaESemGetterNemEstatica()
    {
        var call = await CallAsync("ApiPedidos", "CorposHttp.Derivado");

        Assert.Equal(["Sku", "Quantidade", "Observacao"], call.Fields);
        Assert.False(call.MoreFields);
    }

    [Fact] // EXT-FIELDS-003
    public async Task Extrair_TipoAnonimo_CamposSaoAsPropriedadesDoLiteral()
    {
        var call = await CallAsync("ApiPedidos", "CorposHttp.Anonimo");

        Assert.Equal("PATCH", call.Method);
        Assert.Equal(["id", "motivo"], call.Fields);
        Assert.False(call.MoreFields);
    }

    [Theory] // EXT-FIELDS-004
    [InlineData("CorposHttp.Objeto")]
    [InlineData("CorposHttp.Lista")]
    [InlineData("CorposHttp.Texto")]
    public async Task Extrair_CorpoSemFormaDeObjeto_SemCamposEComMoreFields(string entry)
    {
        var call = await CallAsync("ApiPedidos", entry);

        Assert.Null(call.Fields);
        Assert.True(call.MoreFields);
    }

    [Fact] // EXT-FIELDS-005
    public async Task Extrair_JsonContentCreate_LeOTipoDoArgumento()
    {
        var call = await CallAsync("ApiPedidos", "CorposHttp.Conteudo");

        Assert.Equal(["Sku", "Quantidade"], call.Fields);
        Assert.False(call.MoreFields);
    }

    [Fact] // EXT-FIELDS-006
    public async Task Extrair_GetSemCorpo_SemCamposESemMoreFields()
    {
        var call = await CallAsync("ApiPedidos", "CorposHttp.SemCorpo");

        Assert.Null(call.Fields);
        Assert.False(call.MoreFields);
    }

    [Fact] // EXT-FIELDS-007
    public async Task Extrair_RefitComBodyQueryRotaHeaderECancelamento_SoCorpoQueryERotaEntram()
    {
        var call = await CallAsync("ApiPedidosRefit", "CamposEntrada.Adicionar");

        // Ordem dos parâmetros; o corpo expande nas propriedades; X-Trace e o CancellationToken ficam fora.
        Assert.Equal(["pedidoId", "Sku", "Quantidade", "moeda"], call.Fields);
        Assert.False(call.MoreFields);
    }

    [Fact] // EXT-FIELDS-008
    public async Task Extrair_RefitComBodyDeColecao_SemCamposEComMoreFields()
    {
        var call = await CallAsync("ApiPedidosRefit", "CamposEntrada.Lote");

        Assert.Null(call.Fields);
        Assert.True(call.MoreFields);
    }

    [Fact] // EXT-FIELDS-009
    public async Task Extrair_RefitGetComQueryESemAtributo_ParametrosEntramPeloNome()
    {
        var call = await CallAsync("ApiPedidosRefit", "CamposEntrada.Buscar");

        Assert.Equal(["termo", "pagina"], call.Fields);
        Assert.False(call.MoreFields);
    }

    [Fact] // EXT-FIELDS-010
    public void HttpCall_CamposIguais_SaoIguaisPorValorEOJsonOmiteOsNulos()
    {
        var a = new HttpCall("POST", "api/x", ["a", "b"]);
        Assert.Equal(a, new HttpCall("POST", "api/x", ["a", "b"]));
        Assert.NotEqual(a, new HttpCall("POST", "api/x", ["b", "a"]));
        Assert.NotEqual(a, new HttpCall("POST", "api/x", ["a", "b"], MoreFields: true));

        var json = System.Text.Json.JsonSerializer.Serialize(new HttpCall("GET", "api/x"), FlowGraphJson.Options);
        Assert.DoesNotContain("fields", json, StringComparison.OrdinalIgnoreCase);
    }
}
