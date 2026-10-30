using Rastro.Core.Graph;
using Rastro.Core.Http;

namespace Rastro.Tests.Core;

// Cenários CORE-HTTP-NNN: comparação de rotas HTTP entre quem chama e quem atende (Fase 25).
public class RouteTemplateTests
{
    [Theory] // CORE-HTTP-001
    [InlineData("http://x/api/Pedidos/{Id}?a=1#topo", "api/pedidos/{Id}")]
    [InlineData("https://pagamentos:5000/Api/Estornos/", "api/estornos")]
    [InlineData("/api/estornos/", "api/estornos")]
    [InlineData("~/Health", "health")]
    [InlineData("api/pedidos/{}/Itens", "api/pedidos/{}/itens")]
    public void Normalize_EsquemaHostQueryEBarras_SaemEMinusculasForaDasChaves(string route, string expected)
    {
        Assert.Equal(expected, RouteTemplate.Normalize(route));
    }

    [Theory] // CORE-HTTP-002
    [InlineData("api/pedidos/{id}", "api/pedidos/42", true)]
    [InlineData("api/pedidos/{id:int}/cancelamento", "API/Pedidos/{}/Cancelamento", true)]
    [InlineData("api/estornos", "api/estornos", true)]
    [InlineData("api/pedidos/{id}", "api/pedidos", false)]
    [InlineData("api/pedidos", "api/pedidos/42", false)]
    [InlineData("api/pedidos/{id}/itens", "api/pedidos/42/outros", false)]
    [InlineData("api/v{versao}/estornos", "api/v2/estornos", true)]
    public void Matches_ParametroEBuraco_CasamPorSegmento(string template, string called, bool expected)
    {
        Assert.Equal(expected, RouteTemplate.Matches(template, called));
    }

    [Theory] // CORE-HTTP-003
    [InlineData("files/{*path}", "files/a/b/c", true)]
    [InlineData("files/{*path}", "files", true)]
    [InlineData("files/{*path}", "outros/a", false)]
    public void Matches_CatchAll_CasaComOResto(string template, string called, bool expected)
    {
        Assert.Equal(expected, RouteTemplate.Matches(template, called));
    }

    [Theory] // CORE-HTTP-004
    [InlineData("api/v1/estornos", "v1/estornos", true)]
    [InlineData("api/{tenant}/estornos", "9/estornos", true)]
    [InlineData("api/v1/estornos", "estornos", false)]
    [InlineData("api/estornos", "x/api/estornos", false)]
    [InlineData("api/v1/estornos", "v2/estornos", false)]
    public void MatchesSuffix_FinalDoTemplate_ExigeAoMenosDoisSegmentos(string template, string called, bool expected)
    {
        Assert.Equal(expected, RouteTemplate.MatchesSuffix(template, called));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<RouteEntry>> Indexes() => new Dictionary<string, IReadOnlyList<RouteEntry>>
    {
        ["pagamentos"] =
        [
            new RouteEntry("POST", "api/estornos", "Pag.EstornosController.Criar", "Estornos.cs", 10),
            new RouteEntry("GET", "api/estornos/{id:int}", "Pag.EstornosController.Obter", "Estornos.cs", 20)
        ],
        ["cobranca"] =
        [
            new RouteEntry("POST", "api/v1/cobrancas", "Cob.CobrancasController.Criar", "Cobrancas.cs", 5),
            new RouteEntry("POST", "api/estornos", "Cob.LegadoController.Estornar", "Legado.cs", 8)
        ]
    };

    [Fact] // CORE-HTTP-005
    public void Match_RotaExata_DevolveTodosOsAtendentesEmOrdemDeRepositorioETemplate()
    {
        var found = RouteMatcher.Match(new HttpCall("POST", "api/estornos"), Indexes());

        Assert.Equal(["cobranca", "pagamentos"], found.Select(f => f.Repository));
        Assert.Equal(["Cob.LegadoController.Estornar", "Pag.EstornosController.Criar"], found.Select(f => f.Entry.Target));
    }

    [Fact] // CORE-HTTP-006
    public void Match_VerboDiferente_NaoCasa()
    {
        Assert.Empty(RouteMatcher.Match(new HttpCall("DELETE", "api/estornos"), Indexes()));
        var get = Assert.Single(RouteMatcher.Match(new HttpCall("get", "api/estornos/7"), Indexes()));
        Assert.Equal("Pag.EstornosController.Obter", get.Entry.Target);
    }

    [Fact] // CORE-HTTP-007
    public void Match_SoPorSufixo_UsadoQuandoNenhumExatoCasa()
    {
        var found = Assert.Single(RouteMatcher.Match(new HttpCall("POST", "v1/cobrancas"), Indexes()));

        Assert.Equal("cobranca", found.Repository);
    }

    [Fact] // CORE-HTTP-008
    public void Match_ExatoExisteEnquantoSufixoTambem_FicaSoComOExato()
    {
        var indexes = new Dictionary<string, IReadOnlyList<RouteEntry>>
        {
            ["a"] = [new RouteEntry("GET", "api/x/itens", "A.Sufixo", "a.cs", 1)],
            ["b"] = [new RouteEntry("GET", "x/itens", "B.Exato", "b.cs", 1)]
        };

        var found = Assert.Single(RouteMatcher.Match(new HttpCall("GET", "x/itens"), indexes));

        Assert.Equal("B.Exato", found.Entry.Target);
    }

    [Theory] // CORE-HTTP-009
    [InlineData(null)]
    [InlineData("")]
    public void Match_ChamadaSemRota_NaoCasaComNada(string? route)
    {
        Assert.Empty(RouteMatcher.Match(new HttpCall("POST", route), Indexes()));
    }
}
