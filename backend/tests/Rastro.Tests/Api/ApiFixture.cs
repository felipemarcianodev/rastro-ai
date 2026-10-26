using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rastro.Ai.Providers;
using Rastro.Tests.Ai;
using Microsoft.Extensions.Logging;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Repositories;
using Rastro.Infrastructure.Storage;
using Rastro.Mcp;
using Rastro.Tests.Extractor;

namespace Rastro.Tests.Api;

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

// SQLite em arquivo temporário (o mesmo banco do produto, ADR-002) + um repositório git temporário com uma solution vazia.
// A solution aberta é a fixture Alerts em memória (FixtureSolutionSource): o teste cobre API,
// MCP, analisador e propostas de verdade; só a carga pelo MSBuild fica de fora.
public sealed class ApiFixture : IAsyncLifetime
{
    public const string RepositoryName = "fixture";
    public const string SolutionFile = "Fixture.slnx";

    public string RepositoryRoot { get; } = Path.Combine(Path.GetTempPath(), "rastro-tests", Guid.NewGuid().ToString("N"));

    // Pasta do Rastro só dos testes: nada do ~/.rastro de quem roda (repositórios, licença) entra na suíte.
    private string HomeFolder => Path.Combine(RepositoryRoot + "-home");

    // Pooling desligado: o arquivo fecha de verdade no fim e a pasta temporária pode ser apagada.
    private string ConnectionString => $"Data Source={Path.Combine(HomeFolder, "rastro.db")};Pooling=False";

    public RastroApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(HomeFolder);
        Environment.SetEnvironmentVariable(RastroHome.EnvironmentVariable, HomeFolder);

        Directory.CreateDirectory(RepositoryRoot);
        await File.WriteAllTextAsync(Path.Combine(RepositoryRoot, SolutionFile), "<Solution />");
        // O código da fixture também fica no repositório, nos mesmos caminhos relativos do grafo.
        var alerts = Path.Combine(RepositoryRoot, "Alerts");
        Directory.CreateDirectory(alerts);
        foreach (var file in Directory.GetFiles(Path.Combine(FixtureWorkspace.FixturesRoot, "Alerts")))
            File.Copy(file, Path.Combine(alerts, Path.GetFileName(file)));
        await GitAsync("init", "-q", "-b", "main");
        await GitAsync("config", "user.email", "teste@rastro.local");
        await GitAsync("config", "user.name", "Teste");
        await GitAsync("add", ".");
        await GitAsync("commit", "-q", "-m", "inicial");

        // O repositório de teste entra pelo config.json (como no produto) e não por Rastro:Repositories:0:
        // o config.json entra por último na configuração e, no mesmo índice, sobrescreveria o item 0
        // quando um teste cadastrar outro repositório pela API.
        var added = new RepositoryConfigFile(RastroHome.ConfigPath).Add(RepositoryName, RepositoryRoot);
        Assert.Equal(RepositoryConfigOutcome.Added, added.Outcome);

        Factory = new RastroApiFactory(ConnectionString);
        // Sobe a API uma vez (aplica as migrations) antes de qualquer teste usar o banco direto.
        Factory.CreateClient().Dispose();
    }

    // Os mesmos serviços que o servidor MCP registra, apontando para o banco e o repositório de teste.
    public ServiceProvider McpServices(IDictionary<string, string?>? extraSettings = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Rastro"] = ConnectionString,
            ["Rastro:Repositories:0:Name"] = RepositoryName,
            ["Rastro:Repositories:0:Path"] = RepositoryRoot
        };
        foreach (var (key, value) in extraSettings ?? new Dictionary<string, string?>()) settings[key] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddRastroCore(configuration);
        services.AddSingleton<ISolutionSource, FixtureSolutionSource>();
        services.AddScoped<RastroTools>();
        return services.BuildServiceProvider();
    }

    public async Task NewCommitAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(RepositoryRoot, $"{Guid.NewGuid():N}.txt"), "mudança");
        await GitAsync("add", ".");
        await GitAsync("commit", "-q", "-m", "mudança");
    }

    public async Task<string> HeadAsync() => (await GitAsync("rev-parse", "HEAD")).Output;

    private async Task<ProcessResult> GitAsync(params string[] args)
    {
        var result = await ProcessRunner.RunAsync("git", ["-C", RepositoryRoot, .. args], null, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', args)} falhou: {result.Error}");
        return result;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        foreach (var folder in new[] { RepositoryRoot, HomeFolder })
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Pasta temporária presa por outro processo: o SO limpa depois.
            }
        }
    }
}

// Ancorado num tipo da API (não em Program, que também existe no Rastro.Mcp).
public class RastroApiFactory(string connectionString) : WebApplicationFactory<Rastro.Api.Controllers.HealthController>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Rastro", connectionString);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ISolutionSource, FixtureSolutionSource>();
            // Nenhum teste fala com provedor de verdade.
            services.RemoveAll<IAiTextService>();
            services.AddScoped<IAiTextService, FakeAiTextService>();
        });
    }
}

// Abre a fixture Alerts no lugar da solution do repositório (mesmos caminhos relativos).
public class FixtureSolutionSource : ISolutionSource
{
    public Task<SolutionHandle> OpenAsync(ResolvedTarget target, bool fresh, CancellationToken ct) =>
        Task.FromResult(new SolutionHandle(FixtureWorkspace.Load("Alerts"), FixtureWorkspace.FixturesRoot));
}
