using Rastro.Core.Links;

namespace Rastro.Tests.Core;

// Cenários CORE-LNK-NNN: links para o host do repositório e para o editor (Fase 56).
public class CodeLinksTests
{
    private const string Commit = "abc123";

    private static string? Link(string remote, string path = "src/Api/PedidosController.cs", int line = 42, string? branch = null) =>
        CodeLinks.HostTemplate(CodeLinks.ParseRemote(remote), branch is null ? Commit : null, branch) is { } template
            ? CodeLinks.Fill(template, path, line)
            : null;

    [Theory] // CORE-LNK-001
    [InlineData("https://github.com/loja/api-pedidos.git")]
    [InlineData("https://github.com/loja/api-pedidos")]
    [InlineData("git@github.com:loja/api-pedidos.git")]
    [InlineData("ssh://git@github.com/loja/api-pedidos.git")]
    public void Link_GitHubEmQualquerFormato_ApontaParaOCommitEALinha(string remote)
    {
        Assert.Equal("https://github.com/loja/api-pedidos/blob/abc123/src/Api/PedidosController.cs#L42", Link(remote));
    }

    [Fact] // CORE-LNK-002
    public void Link_RemoteComUsuarioETokenNaUrl_NuncaLevaACredencial()
    {
        var link = Link("https://usuario:ghp_segredo@github.com/loja/api-pedidos.git");

        Assert.Equal("https://github.com/loja/api-pedidos/blob/abc123/src/Api/PedidosController.cs#L42", link);
        Assert.DoesNotContain("segredo", link);
        Assert.DoesNotContain("usuario", link);
    }

    [Theory] // CORE-LNK-003
    [InlineData("https://usuario@dev.azure.com/empresa/Loja/_git/api-pedidos")]
    [InlineData("git@ssh.dev.azure.com:v3/empresa/Loja/api-pedidos")]
    [InlineData("https://empresa.visualstudio.com/Loja/_git/api-pedidos")]
    public void Link_AzureDevOpsHttpsSshEEnderecoAntigo_MesmoLinkPeloCommit(string remote)
    {
        Assert.Equal(
            "https://dev.azure.com/empresa/Loja/_git/api-pedidos?path=/src/Api/PedidosController.cs&version=GCabc123" +
            "&line=42&lineEnd=42&lineStartColumn=1&lineEndColumn=200&lineStyle=plain&_a=contents",
            Link(remote));
    }

    [Fact] // CORE-LNK-004
    public void Link_AzureDevOpsPelaBranch_UsaGB()
    {
        var link = Link("https://dev.azure.com/empresa/Loja/_git/api-pedidos", branch: "main");

        Assert.Contains("&version=GBmain&", link);
    }

    [Fact] // CORE-LNK-005
    public void Link_GitLabComSubgrupoEBitbucket_FormatoDeCadaHost()
    {
        Assert.Equal("https://gitlab.com/loja/backend/api-pedidos/-/blob/abc123/a.cs#L3", Link("git@gitlab.com:loja/backend/api-pedidos.git", "a.cs", 3));
        Assert.Equal("https://bitbucket.org/loja/api-pedidos/src/abc123/a.cs#lines-3", Link("https://bitbucket.org/loja/api-pedidos.git", "a.cs", 3));
    }

    [Theory] // CORE-LNK-006
    [InlineData("https://git.empresa.local/loja/api-pedidos.git")]
    [InlineData("")]
    [InlineData("C:\\repositorios\\api-pedidos")]
    [InlineData("https://dev.azure.com/empresa/sem-git")]
    public void Link_RemoteDesconhecidoOuIncompleto_SemLinkParaOHost(string remote)
    {
        Assert.Null(Link(remote));
    }

    [Fact] // CORE-LNK-007
    public void Link_SemCommitNemBranch_SemLink()
    {
        Assert.Null(CodeLinks.HostTemplate(CodeLinks.ParseRemote("https://github.com/loja/api"), null, null));
    }

    [Fact] // CORE-LNK-008
    public void Fill_CaminhoComEspacoEAcento_CodificaCadaSegmentoEMantemABarra()
    {
        var link = Link("https://github.com/loja/api", "src/Serviços de Pedido/Cancelamento.cs", 7);

        Assert.Equal("https://github.com/loja/api/blob/abc123/src/Servi%C3%A7os%20de%20Pedido/Cancelamento.cs#L7", link);
    }

    [Fact] // CORE-LNK-009
    public void EditorTemplate_PadraoVsCodeNoWindowsENoLinux_RaizComBarraEEspacoCodificado()
    {
        Assert.Equal("vscode://file/C:/Meus%20Projetos/loja/a.cs:12",
            CodeLinks.Fill(CodeLinks.EditorTemplate(@"C:\Meus Projetos\loja\", null), "a.cs", 12));
        Assert.Equal("vscode://file/home/dev/loja/src/a.cs:3",
            CodeLinks.Fill(CodeLinks.EditorTemplate("/home/dev/loja", null), "src/a.cs", 3));
    }

    [Fact] // CORE-LNK-010
    public void EditorTemplate_ModeloConfigurado_PathVemAbsoluto()
    {
        var template = CodeLinks.EditorTemplate(@"C:\loja", "rider://open?file={path}&line={line}");

        Assert.Equal("rider://open?file=C:/loja/src/a.cs&line=9", CodeLinks.Fill(template, "src/a.cs", 9));
    }
}
