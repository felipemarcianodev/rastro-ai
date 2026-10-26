using Microsoft.Extensions.Options;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Tests.Api;

// Cenários CAT-SOL-NNN: a lista de solutions que a tela sugere no levantamento novo.
public sealed class RepositoryCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rastro-cat-" + Guid.NewGuid().ToString("N"));

    private RepositoryCatalog Catalog()
    {
        var options = new RastroOptions { Repositories = [new RepositoryOptions { Name = "loja", Path = _root }] };
        return new RepositoryCatalog(new FixedMonitor(options));
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
