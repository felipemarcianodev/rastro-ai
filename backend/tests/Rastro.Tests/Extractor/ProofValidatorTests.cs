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
