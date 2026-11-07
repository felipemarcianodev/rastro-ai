using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Rastro.Api.Desktop;
using Xunit;

namespace Rastro.Tests.Api;

// Cenários API-JAN-NNN: janela própria ao digitar "rastro" (ADR-007).
public class DesktopTests
{
    [Fact] // API-JAN-001
    public void Decide_PacoteRodandoDeVerdade_AbreAJanela()
    {
        Assert.Equal(WindowSkipReason.None, DesktopLaunch.Decide(standalone: true, configuredUrls: false, noWindow: false, hasEmbeddedScreen: true));
    }

    [Fact] // API-JAN-002
    public void Decide_DentroDosTestes_NaoAbre()
    {
        Assert.Equal(WindowSkipReason.NotStandalone, DesktopLaunch.Decide(standalone: false, configuredUrls: false, noWindow: false, hasEmbeddedScreen: true));
    }

    [Fact] // API-JAN-003
    public void Decide_ComUrlsConfiguradas_NaoAbre()
    {
        Assert.Equal(WindowSkipReason.ConfiguredUrls, DesktopLaunch.Decide(standalone: true, configuredUrls: true, noWindow: false, hasEmbeddedScreen: true));
    }

    [Fact] // API-JAN-004
    public void Decide_ComSemJanela_NaoAbre()
    {
        Assert.Equal(WindowSkipReason.NoWindowFlag, DesktopLaunch.Decide(standalone: true, configuredUrls: false, noWindow: true, hasEmbeddedScreen: true));
    }

    [Fact] // API-JAN-005
    public void Decide_SemTelaEmbutida_NaoAbre()
    {
        Assert.Equal(WindowSkipReason.NoEmbeddedScreen, DesktopLaunch.Decide(standalone: true, configuredUrls: false, noWindow: false, hasEmbeddedScreen: false));
    }

    [Fact] // API-JAN-006
    public void ExtractFlag_SemJanelaEmQualquerPosicao_SaiDosArgumentosDoHost()
    {
        var (noWindow, hostArgs) = DesktopLaunch.ExtractFlag(["--sem-janela", "--porta", "7460"]);

        Assert.True(noWindow);
        Assert.Equal(["--porta", "7460"], hostArgs);
        Assert.False(DesktopLaunch.ExtractFlag(["--porta", "7460"]).NoWindow);
    }

    [Fact] // API-JAN-007
    public void Handle_PedidoDePasta_DevolveOCaminhoEscolhido()
    {
        var response = DesktopBridge.Handle("""{"type":0,"requestId":"pasta-1"}""", () => @"C:\codigo\loja", NullLogger.Instance);

        using var json = JsonDocument.Parse(response!);
        Assert.Equal((int)DesktopMessageType.FolderPicked, json.RootElement.GetProperty("type").GetInt32());
        Assert.Equal("pasta-1", json.RootElement.GetProperty("requestId").GetString());
        Assert.Equal(@"C:\codigo\loja", json.RootElement.GetProperty("path").GetString());
    }

    [Fact] // API-JAN-008
    public void Handle_UsuarioCancelou_DevolvePathNull()
    {
        var response = DesktopBridge.Handle("""{"type":0,"requestId":"pasta-2"}""", () => null, NullLogger.Instance);

        using var json = JsonDocument.Parse(response!);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("path").ValueKind);
    }

    [Theory] // API-JAN-009
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("[0]")]
    [InlineData("""{"type":1,"requestId":"pasta-1"}""")]
    [InlineData("""{"type":7,"requestId":"pasta-1"}""")]
    [InlineData("""{"type":"0","requestId":"pasta-1"}""")]
    [InlineData("""{"type":0,"requestId":"../../etc"}""")]
    [InlineData("""{"type":0}""")]
    [InlineData("""{"type":0,"requestId":"pasta-1","command":"cmd /c calc"}""")]
    public void Handle_MensagemForaDoFormato_RecusaSemAbrirACaixa(string message)
    {
        var opened = false;

        var response = DesktopBridge.Handle(message, () => { opened = true; return @"C:\x"; }, NullLogger.Instance);

        Assert.Null(response);
        Assert.False(opened);
    }

    [Fact] // API-JAN-010
    public void Handle_MensagemGrandeDemais_Recusa()
    {
        var message = $$"""{"type":0,"requestId":"{{new string('a', DesktopBridge.MaxMessageLength)}}"}""";

        Assert.Null(DesktopBridge.Handle(message, () => @"C:\x", NullLogger.Instance));
    }

    [Fact] // API-JAN-011
    public void FolderPickedMessage_CaminhoRelativo_VaiComoNull()
    {
        using var json = JsonDocument.Parse(DesktopBridge.FolderPickedMessage("pasta-1", @"codigo\loja"));

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("path").ValueKind);
    }
}
