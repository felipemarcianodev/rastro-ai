using Microsoft.Extensions.Logging.Abstractions;
using Rastro.Api.Desktop;
using Xunit;

namespace Rastro.Tests.Api;

// Cenários DESK-LNK-NNN: links que a janela própria aceita abrir (Fase 57, ADR-010).
public class ExternalLinkPolicyTests
{
    private const string Template = "vs://open?path={path}&line={line}";

    [Theory] // DESK-LNK-001
    [InlineData("https://github.com/org/repo/blob/abc/src/A.cs#L10")]
    [InlineData("https://gitlab.com/grupo/sub/repo/-/blob/abc/A.cs#L10")]
    [InlineData("https://bitbucket.org/org/repo/src/abc/A.cs#lines-10")]
    [InlineData("https://dev.azure.com/org/proj/_git/repo?path=/A.cs&version=GCabc&line=10&_a=contents")]
    [InlineData("HTTPS://GitHub.com/org/repo")]
    public void IsAllowed_HostReconhecido_Aceita(string url)
    {
        Assert.True(ExternalLinkPolicy.IsAllowed(url, null));
    }

    [Fact] // DESK-LNK-002
    public void IsAllowed_VsCodeFile_Aceita()
    {
        Assert.True(ExternalLinkPolicy.IsAllowed("vscode://file/C:/codigo/loja/src/A.cs:10", null));
    }

    [Theory] // DESK-LNK-003
    [InlineData("http://github.com/org/repo")]
    [InlineData("https://github.com.evil.com/org/repo")]
    [InlineData("https://evilgithub.com/org/repo")]
    [InlineData("https://sub.github.com/org/repo")]
    [InlineData("https://github.com./org/repo")]
    [InlineData("https://user@github.com/org/repo")]
    [InlineData("https://user:senha@github.com/org/repo")]
    [InlineData("https://github.com@evil.com/org/repo")]
    [InlineData("https://github.com:8443/org/repo")]
    [InlineData("https://github.com:443/org/repo")]
    [InlineData("https://github.com\\@evil.com/")]
    [InlineData("https://github.com/org/repo\r\nX: y")]
    [InlineData("https://github.com/org repo")]
    public void IsAllowed_HostOuFormaForaDaRegra_Recusa(string url)
    {
        Assert.False(ExternalLinkPolicy.IsAllowed(url, null));
    }

    [Theory] // DESK-LNK-004
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>1</script>")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("vscode://other/x")]
    [InlineData("vscode-insiders://file/C:/x")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("calc.exe")]
    [InlineData("://sem-esquema")]
    public void IsAllowed_EsquemaPerigosoOuDesconhecido_Recusa(string url)
    {
        Assert.False(ExternalLinkPolicy.IsAllowed(url, Template));
    }

    [Fact] // DESK-LNK-005
    public void IsAllowed_EsquemaDoEditorSemConfigurar_Recusa()
    {
        Assert.False(ExternalLinkPolicy.IsAllowed("vs://open?path=C:/x/A.cs&line=3", null));
        Assert.False(ExternalLinkPolicy.IsAllowed("vs://open?path=C:/x/A.cs&line=3", "  "));
    }

    [Fact] // DESK-LNK-006
    public void IsAllowed_EsquemaDoEditorConfigurado_Aceita()
    {
        Assert.True(ExternalLinkPolicy.IsAllowed("vs://open?path=C:/x/A.cs&line=3", Template));
        Assert.True(ExternalLinkPolicy.IsAllowed("VS://open?path=C:/x/A.cs&line=3", Template));
        Assert.False(ExternalLinkPolicy.IsAllowed("rider://open?file=C:/x/A.cs", Template));
    }

    [Theory] // DESK-LNK-007
    [InlineData("http://x/{path}")]
    [InlineData("https://evil.com/{path}")]
    [InlineData("file:///{path}")]
    [InlineData("javascript:{path}")]
    [InlineData("data:{path}")]
    [InlineData("ms-settings:{path}")]
    [InlineData("{path}")]
    [InlineData("")]
    public void IsAllowed_ModeloComEsquemaProibido_NaoLiberaOEsquema(string template)
    {
        var scheme = template.Split(':')[0];
        Assert.False(ExternalLinkPolicy.IsAllowed($"{scheme}://x/a", template));
        Assert.False(ExternalLinkPolicy.IsAllowed("file:///C:/a", template));
    }

    [Theory] // DESK-LNK-008
    [InlineData(null)]
    [InlineData("")]
    [InlineData("não é url")]
    [InlineData("https://")]
    [InlineData("https:///github.com")]
    public void IsAllowed_UrlInvalida_Recusa(string? url)
    {
        Assert.False(ExternalLinkPolicy.IsAllowed(url, Template));
    }

    [Fact] // DESK-LNK-009
    public void IsAllowed_UrlGrandeDemais_Recusa()
    {
        var url = "https://github.com/" + new string('a', ExternalLinkPolicy.MaxLength);

        Assert.False(ExternalLinkPolicy.IsAllowed(url, null));
    }

    [Fact] // DESK-LNK-010
    public void Describe_UrlComCaminhoDoDisco_DevolveSoEsquemaEHost()
    {
        var (scheme, host) = ExternalLinkPolicy.Describe("https://Evil.com/C:/Users/dono/segredo");

        Assert.Equal("https", scheme);
        Assert.Equal("evil.com", host);
        Assert.Equal(("?", "?"), ExternalLinkPolicy.Describe("não é url"));
    }
}

// Cenários DESK-LNK-NNN: a mensagem "abrir link" da ponte da janela.
public class DesktopOpenLinkTests
{
    private static string Message(string url) => System.Text.Json.JsonSerializer.Serialize(new { type = (int)DesktopMessageType.OpenLink, url });

    [Fact] // DESK-LNK-011
    public void Handle_AbrirLinkPermitido_AbreSemResponderAPagina()
    {
        string? opened = null;

        var response = DesktopBridge.Handle(Message("https://github.com/org/repo"), () => null, NullLogger.Instance, url => opened = url);

        Assert.Null(response);
        Assert.Equal("https://github.com/org/repo", opened);
    }

    [Theory] // DESK-LNK-012
    [InlineData("http://github.com/org/repo")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("vs://open?path=C:/x")]
    [InlineData("https://github.com.evil.com/x")]
    public void Handle_AbrirLinkNaoPermitido_RecusaSemAbrir(string url)
    {
        var opened = false;

        var response = DesktopBridge.Handle(Message(url), () => null, NullLogger.Instance, _ => opened = true);

        Assert.Null(response);
        Assert.False(opened);
    }

    [Fact] // DESK-LNK-013
    public void Handle_AbrirLinkComEditorConfigurado_AbreOEsquemaDoEditor()
    {
        string? opened = null;

        DesktopBridge.Handle(Message("vs://open?path=C:/x/A.cs&line=3"), () => null, NullLogger.Instance, url => opened = url, "vs://open?path={path}&line={line}");

        Assert.Equal("vs://open?path=C:/x/A.cs&line=3", opened);
    }

    [Theory] // DESK-LNK-014
    [InlineData("""{"type":2}""")]
    [InlineData("""{"type":2,"url":5}""")]
    [InlineData("""{"type":2,"url":"https://github.com/a/b","extra":1}""")]
    [InlineData("""{"type":2,"url":""}""")]
    [InlineData("""{"type":2,"requestId":"x","url":"https://github.com/a/b"}""")]
    public void Handle_AbrirLinkForaDoFormato_RecusaSemAbrir(string message)
    {
        var opened = false;

        var response = DesktopBridge.Handle(message, () => null, NullLogger.Instance, _ => opened = true);

        Assert.Null(response);
        Assert.False(opened);
    }

    [Fact] // DESK-LNK-015
    public void Handle_AbrirLinkSemAbridor_Recusa()
    {
        Assert.Null(DesktopBridge.Handle(Message("https://github.com/org/repo"), () => null, NullLogger.Instance));
    }

    [Fact] // DESK-LNK-016
    public void Handle_PedidoDePastaContinuaFuncionando()
    {
        var response = DesktopBridge.Handle("""{"type":0,"requestId":"pasta-1"}""", () => @"C:\codigo", NullLogger.Instance, _ => { });

        Assert.NotNull(response);
    }
}
