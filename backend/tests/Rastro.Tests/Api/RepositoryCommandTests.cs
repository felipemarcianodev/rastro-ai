using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Api.Cli;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Tests.Api;

// Cenários CLI-REPO-NNN: "rastro repo" grava o config.json da pasta do Rastro (ADR-002).
public sealed class RepositoryCommandTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rastro-cli-" + Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_folder, "home", "config.json");

    private string NewRepository(string name)
    {
        var path = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        return path;
    }

    private (int Code, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = RepositoryCommand.Run(args, ConfigPath, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Fact] // CLI-REPO-001
    public void Add_GravaNomeCaminhoEComandos_EListMostra()
    {
        var path = NewRepository("loja");

        var added = Run("add", "loja", path, "--build", "dotnet build Loja.slnx", "--testes", "dotnet test Loja.slnx");
        var listed = Run("list");

        Assert.Equal(0, added.Code);
        Assert.Contains($"loja  {path}", listed.Output);
        var configuration = new ConfigurationBuilder().AddJsonFile(ConfigPath).Build();
        var options = configuration.GetSection(RastroOptions.Section).Get<RastroOptions>()!;
        var repo = Assert.Single(options.Repositories);
        Assert.Equal(("loja", path, "dotnet build Loja.slnx", "dotnet test Loja.slnx"), (repo.Name, repo.Path, repo.BuildCommand, repo.TestCommand));
    }

    [Fact] // CLI-REPO-002
    public void Add_NomeRepetidoNomeInvalidoOuPastaInexistente_Recusa()
    {
        var path = NewRepository("loja");
        Run("add", "loja", path);

        var repeated = Run("add", "LOJA", path);
        var badName = Run("add", "../fora", path);
        var missing = Run("add", "outro", Path.Combine(_folder, "nao-existe"));

        Assert.Equal(1, repeated.Code);
        Assert.Contains("Já existe", repeated.Error);
        Assert.Equal(1, badName.Code);
        Assert.Contains("Nome inválido", badName.Error);
        Assert.Equal(1, missing.Code);
        Assert.Contains("não existe", missing.Error);
        Assert.Single(new ConfigurationBuilder().AddJsonFile(ConfigPath).Build()
            .GetSection("Rastro:Repositories").GetChildren());
    }

    [Fact] // CLI-REPO-003
    public void Remove_TiraDaLista_ENomeDesconhecidoAvisa()
    {
        Run("add", "loja", NewRepository("loja"));
        Run("add", "erp", NewRepository("erp"));

        var removed = Run("remove", "loja");
        var unknown = Run("remove", "loja");

        Assert.Equal(0, removed.Code);
        Assert.Equal(1, unknown.Code);
        Assert.DoesNotContain("loja", Run("list").Output);
        Assert.Contains("erp", Run("list").Output);
    }

    [Fact] // CLI-REPO-004
    public async Task Catalogo_VeORepositorioNovoSemReiniciar()
    {
        Run("add", "loja", NewRepository("loja"));
        var configuration = new ConfigurationBuilder().AddJsonFile(ConfigPath, optional: true, reloadOnChange: true).Build();
        var services = new ServiceCollection();
        services.Configure<RastroOptions>(configuration.GetSection(RastroOptions.Section));
        services.AddSingleton<Rastro.Infrastructure.Analysis.INodeRuntime>(new Rastro.Tests.Infrastructure.FakeNodeRuntime(false));
        services.AddSingleton<RepositoryCatalog>();
        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<RepositoryCatalog>();
        Assert.Equal(["loja"], catalog.Names());

        Run("add", "erp", NewRepository("erp"));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (catalog.Names().Count < 2 && DateTime.UtcNow < deadline) await Task.Delay(100);
        Assert.Equal(["erp", "loja"], catalog.Names());
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
