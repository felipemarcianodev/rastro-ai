using Rastro.Core.Graph;
using Rastro.Core.Http;
using Rastro.Extractor;

namespace Rastro.Tests.Extractor;

// Cenários EXT-PROOF-NNN: o compilador confere a prova de uma proposta (ADR-001).
public class ProofValidatorTests
{
    private const string Services = "Alerts/Services.cs";

    private static Task<ProofVerdict> ValidateAsync(string target, string file, int line) =>
        ProofValidator.ValidateAsync(FixtureWorkspace.Load("Alerts"), FixtureWorkspace.FixturesRoot, new EdgeProof(target, file, line));

    private static int EmailNotifyLine => FixtureLines.LineOf(Services, "public class EmailNotifier", "public void Notify");

    [Fact] // EXT-PROOF-001
    public async Task Validar_MetodoNoArquivoENaLinhaCertos_Aceita()
    {
        var verdict = await ValidateAsync("EmailNotifier.Notify", Services, EmailNotifyLine);

        Assert.True(verdict.Accepted, verdict.Message);
        Assert.Equal("Fixture.Alerts.EmailNotifier.Notify()", verdict.TargetDisplay);
    }

    [Fact] // EXT-PROOF-002
    public async Task Validar_LinhaForaDaDeclaracao_RecusaDizendoOndeEsta()
    {
        var verdict = await ValidateAsync("EmailNotifier.Notify", Services, 1);

        Assert.False(verdict.Accepted);
        Assert.Contains("fora", verdict.Message);
    }

    [Fact] // EXT-PROOF-003
    public async Task Validar_ArquivoErrado_RecusaDizendoOArquivoCerto()
    {
        var verdict = await ValidateAsync("EmailNotifier.Notify", "Alerts/AlertJob.cs", EmailNotifyLine);

        Assert.False(verdict.Accepted);
        Assert.Contains(Services, verdict.Message);
    }

    [Theory] // EXT-PROOF-004
    [InlineData("MetodoInventado.Fazer")]
    [InlineData("EmailNotifier.Inexistente")]
    [InlineData("SemPonto")]
    public async Task Validar_MetodoQueNaoExiste_Recusa(string target)
    {
        var verdict = await ValidateAsync(target, Services, EmailNotifyLine);

        Assert.False(verdict.Accepted);
        Assert.Contains("não confere", verdict.Message);
    }

    [Theory] // EXT-PROOF-005
    [InlineData("", Services, 10)]
    [InlineData("EmailNotifier.Notify", "", 10)]
    [InlineData("EmailNotifier.Notify", Services, 0)]
    public async Task Validar_ProvaIncompleta_Recusa(string target, string file, int line)
    {
        var verdict = await ValidateAsync(target, file, line);

        Assert.False(verdict.Accepted);
        Assert.Contains("incompleta", verdict.Message);
    }

    private const string Estornos = "ApiPagamentos/EstornosController.cs";

    private static readonly IReadOnlyList<RouteEntry> PagamentosIndex =
        RouteIndexBuilder.Build(Path.Combine(FixtureWorkspace.FixturesRoot, "ApiPagamentos"));

    private static Task<ProofVerdict> ValidateHttpAsync(string target, string file, int line, HttpCall call) =>
        ProofValidator.ValidateHttpAsync(FixtureWorkspace.Load("ApiPagamentos"), FixtureWorkspace.FixturesRoot,
            new EdgeProof(target, file, line), call, PagamentosIndex);

    private static int CriarLine => FixtureLines.LineOf(Estornos, "[HttpPost]", "public async Task Criar");

    [Fact] // EXT-PROOF-007
    public async Task ValidarHttp_DestinoNoIndiceComVerboERotaQueCasam_Aceita()
    {
        var verdict = await ValidateHttpAsync("EstornosController.Criar", Estornos, CriarLine, new HttpCall("POST", "api/estornos"));

        Assert.True(verdict.Accepted, verdict.Message);
    }

    [Theory] // EXT-PROOF-008
    [InlineData("POST", null)]
    [InlineData("SEND", null)]
    [InlineData("SEND", "api/estornos")]
    public async Task ValidarHttp_VerboDesconhecidoOuRotaDesconhecida_ExigeSoOQueSabe(string method, string? route)
    {
        var verdict = await ValidateHttpAsync("EstornosController.Criar", Estornos, CriarLine, new HttpCall(method, route));

        Assert.True(verdict.Accepted, verdict.Message);
    }

    [Fact] // EXT-PROOF-009
    public async Task ValidarHttp_VerboDiferenteDoDestino_Recusa()
    {
        var verdict = await ValidateHttpAsync("EstornosController.Criar", Estornos, CriarLine, new HttpCall("GET", "api/estornos"));

        Assert.False(verdict.Accepted);
        Assert.Contains("verbo", verdict.Message);
    }

    [Fact] // EXT-PROOF-010
    public async Task ValidarHttp_RotaQueNaoCasa_RecusaDizendoOQueOMetodoAtende()
    {
        var verdict = await ValidateHttpAsync("EstornosController.Criar", Estornos, CriarLine, new HttpCall("POST", "api/outra/coisa"));

        Assert.False(verdict.Accepted);
        Assert.Contains("api/estornos", verdict.Message);
        Assert.Contains("não casa", verdict.Message);
    }

    [Fact] // EXT-PROOF-011
    public async Task ValidarHttp_MetodoQueNaoEUmDestinoHttp_Recusa()
    {
        var line = FixtureLines.LineOf(Estornos, "class GatewayCartao", "public async Task EstornarAsync");

        var verdict = await ValidateHttpAsync("GatewayCartao.EstornarAsync", Estornos, line, new HttpCall("POST", "api/estornos"));

        Assert.False(verdict.Accepted);
        Assert.Contains("índice de rotas", verdict.Message);
    }

    [Fact] // EXT-PROOF-012
    public async Task ValidarHttp_ProvaNormalErrada_RecusaAntesDoIndice()
    {
        var verdict = await ValidateHttpAsync("EstornosController.Criar", Estornos, 1, new HttpCall("POST", "api/estornos"));

        Assert.False(verdict.Accepted);
        Assert.Contains("fora", verdict.Message);
    }

    [Fact] // EXT-PROOF-006
    public async Task BuscarMetodos_PorNome_DevolveArquivoELinhaQueServemDeProva()
    {
        var found = await ProofValidator.SearchMethodsAsync(FixtureWorkspace.Load("Alerts"), FixtureWorkspace.FixturesRoot, "Notify", 30);

        var email = Assert.Single(found, f => f.Name == "Fixture.Alerts.EmailNotifier.Notify");
        Assert.Equal(Services, email.File);
        Assert.Equal(EmailNotifyLine, email.Line);
        Assert.Contains(found, f => f.Name == "Fixture.Alerts.SmsNotifier.Notify");
        Assert.DoesNotContain(found, f => f.Name.StartsWith("Fixture.Alerts.INotifier", StringComparison.Ordinal));
    }
}
