using Microsoft.Extensions.Options;
using Rastro.Core.Graph;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Repositories;
using Rastro.Tests.Infrastructure;

namespace Rastro.Tests.Api;

// Cenários CAT-SOL-NNN: a lista de solutions que a tela sugere no levantamento novo.
public sealed class RepositoryCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rastro-cat-" + Guid.NewGuid().ToString("N"));

    private RepositoryCatalog Catalog(bool node = false)
    {
        var options = new RastroOptions { Repositories = [new RepositoryOptions { Name = "loja", Path = _root }] };
        return new RepositoryCatalog(new FixedMonitor(options), new FakeNodeRuntime(node));
    }

    private void Touch(string relative)
    {
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "");
    }

    [Fact] // CAT-SOL-001
    public void Solutions_ComSlnESlnx_MostraSoAsSolutions()
    {
        Touch("Loja.sln");
        Touch("backend/Loja.slnx");
        Touch("backend/src/Loja.Api/Loja.Api.csproj");
        Touch("backend/src/Loja.Core/Loja.Core.csproj");

        var solutions = Catalog().Solutions("loja");

        Assert.Equal(["Loja.sln", "backend/Loja.slnx"], solutions);
    }

    [Fact] // CAT-SOL-002
    public void Solutions_SemNenhumaSolution_MostraOsProjetos()
    {
        Touch("src/Loja.Api/Loja.Api.csproj");

        var solutions = Catalog().Solutions("loja");

        Assert.Equal(["src/Loja.Api/Loja.Api.csproj"], solutions);
    }

    [Fact] // CAT-SOL-003
    public void Resolve_CsprojContinuaAceito_MesmoComSolutionNoRepositorio()
    {
        Touch("Loja.sln");
        Touch("src/Loja.Api/Loja.Api.csproj");

        var result = Catalog().Resolve("loja", "src/Loja.Api/Loja.Api.csproj");

        Assert.Null(result.Error);
    }

    [Fact] // CAT-SOL-004
    public void Resolve_TsconfigDentroDaRaiz_AceitaEMarcaOAngular()
    {
        Touch("tsconfig.json");
        Touch("apps/web/tsconfig.app.json");

        var raiz = Catalog().Resolve("loja", "tsconfig.json");
        var app = Catalog().Resolve("loja", "apps/web/tsconfig.app.json");

        Assert.Null(raiz.Error);
        Assert.Equal(SourceLanguage.Angular, raiz.Target!.Language);
        Assert.Equal("apps/web/tsconfig.app.json", app.Target!.SolutionRelativePath);
        Assert.Equal(SourceLanguage.Angular, app.Target.Language);
    }

    [Fact] // CAT-SOL-005
    public void Resolve_SolutionContinuaDotNet()
    {
        Touch("Loja.sln");

        Assert.Equal(SourceLanguage.DotNet, Catalog().Resolve("loja", "Loja.sln").Target!.Language);
    }

    [Theory] // CAT-SOL-006
    [InlineData("../fora/tsconfig.json")]
    [InlineData("package.json")]
    [InlineData("tsconfig.txt")]
    [InlineData("meutsconfig.json")]
    public void Resolve_TsconfigForaDaRaizOuArquivoQueNaoEOTsconfig_RecusaCaminhoInvalido(string path)
    {
        Touch("package.json");

        var result = Catalog().Resolve("loja", path);

        Assert.Equal(CatalogError.SolutionPathInvalid, result.Error);
    }

    [Fact] // CAT-SOL-007
    public void Resolve_TsconfigAbsoluto_RecusaCaminhoInvalido()
    {
        Touch("tsconfig.json");

        var result = Catalog().Resolve("loja", Path.Combine(_root, "tsconfig.json"));

        Assert.Equal(CatalogError.SolutionPathInvalid, result.Error);
    }

    [Fact] // CAT-SOL-008
    public void Resolve_TsconfigQueNaoExiste_RetornaNaoEncontrado()
    {
        Assert.Equal(CatalogError.SolutionNotFound, Catalog().Resolve("loja", "tsconfig.json").Error);
    }

    [Fact] // CAT-SOL-009
    public void Solutions_PastaComAngularJsonENodeDisponivel_ListaOTsconfigApp()
    {
        Touch("Loja.sln");
        Touch("web/angular.json");
        Touch("web/tsconfig.json");
        Touch("web/tsconfig.app.json");
        Touch("web/tsconfig.spec.json");

        var solutions = Catalog(node: true).Solutions("loja");

        Assert.Equal(["Loja.sln", "web/tsconfig.app.json"], solutions);
        Assert.Equal(["Loja.sln"], Catalog(node: true).DotNetSolutions("loja"));
    }

    [Fact] // CAT-SOL-010
    public void Solutions_AngularJsonSemTsconfigApp_UsaOTsconfigJson()
    {
        Touch("web/angular.json");
        Touch("web/tsconfig.json");

        Assert.Equal(["web/tsconfig.json"], Catalog(node: true).Solutions("loja"));
    }

    [Fact] // CAT-SOL-011
    public void Solutions_SemNode_NaoOfereceOTsconfig()
    {
        Touch("Loja.sln");
        Touch("web/angular.json");
        Touch("web/tsconfig.app.json");

        Assert.Equal(["Loja.sln"], Catalog(node: false).Solutions("loja"));
    }

    [Fact] // CAT-SOL-012
    public void Solutions_TsconfigSemAngularJson_NaoEntra()
    {
        Touch("lib/tsconfig.json");

        Assert.Empty(Catalog(node: true).Solutions("loja")!);
    }

    [Theory] // CAT-SRC-001
    [InlineData("src/app/a.component.ts", true)]
    [InlineData("src/app/a.component.html", true)]
    [InlineData("Controller.cs", true)]
    [InlineData("package.json", false)]
    [InlineData("../x.ts", false)]
    public void SourceFile_ExtensoesDeCodigo_AceitaCsTsEHtmlDentroDaRaiz(string path, bool accepted)
    {
        Assert.Equal(accepted, Catalog().SourceFile("loja", path) is not null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FixedMonitor(RastroOptions value) : IOptionsMonitor<RastroOptions>
    {
        public RastroOptions CurrentValue => value;
        public RastroOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<RastroOptions, string?> listener) => null;
    }
}
