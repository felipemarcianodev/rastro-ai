using Microsoft.Extensions.DependencyInjection;
using Rastro.Core.Surveys;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Repositories;
using Rastro.Tests.Api;
using Rastro.Tests.Extractor;

namespace Rastro.Tests.Infrastructure;

// Repositório git temporário com o código de uma fixture (ApiPedidos, ApiPagamentos...): a costura entre
// sistemas (Fase 25) lê o índice de rotas dos .cs da raiz e o commit pelo git, como no produto.
// A solution aberta é a fixture em memória (FixtureSolutionSource); o .slnx só precisa existir.
internal sealed class CrossRepo : IAsyncDisposable
{
    public string Name { get; }
    public string Fixture { get; }
    public string Root { get; }
    public string Solution => Name + ".slnx";

    private CrossRepo(string name, string fixture, string root)
    {
        Name = name;
        Fixture = fixture;
        Root = root;
    }

    public static async Task<CrossRepo> CreateAsync(string prefix, string fixture, bool git = true)
    {
        var name = $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 7)];
        var root = Path.Combine(Path.GetTempPath(), "rastro-tests", "cross-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(root);
        foreach (var file in Directory.GetFiles(Path.Combine(FixtureWorkspace.FixturesRoot, fixture), "*.cs"))
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        await File.WriteAllTextAsync(Path.Combine(root, name + ".slnx"), "<Solution />");

        var repo = new CrossRepo(name, fixture, root);
        if (git)
        {
            await repo.GitAsync("init", "-q", "-b", "main");
            await repo.GitAsync("config", "user.email", "teste@rastro.local");
            await repo.GitAsync("config", "user.name", "Teste");
            await repo.GitAsync("add", ".");
            await repo.GitAsync("commit", "-q", "-m", "inicial");
        }
        FixtureSolutionSource.Use(name, fixture);
        return repo;
    }

    public async Task NewCommitAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, $"{Guid.NewGuid():N}.txt"), "mudança");
        await GitAsync("add", ".");
        await GitAsync("commit", "-q", "-m", "mudança");
    }

    public async Task<string> HeadAsync() => (await GitAsync("rev-parse", "HEAD")).Output;

    private async Task<ProcessResult> GitAsync(params string[] args)
    {
        var result = await ProcessRunner.RunAsync("git", ["-C", Root, .. args], null, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', args)} falhou: {result.Error}");
        return result;
    }

    public ValueTask DisposeAsync()
    {
        FixtureSolutionSource.Forget(Name);
        try
        {
            var folder = Path.GetDirectoryName(Root)!;
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // Pasta temporária presa por outro processo: o SO limpa depois.
        }
        return ValueTask.CompletedTask;
    }
}

internal static class CrossKit
{
    // Os serviços do Rastro (os mesmos do servidor MCP) com os repositórios de teste cadastrados e a
    // fonte de solutions que o teste passar (para conferir abertura e liberação).
    public static ServiceProvider Services(ApiFixture fixture, FixtureSolutionSource source, params CrossRepo[] repos)
    {
        var settings = new Dictionary<string, string?>();
        for (var i = 0; i < repos.Length; i++)
        {
            settings[$"Rastro:Repositories:{i + 1}:Name"] = repos[i].Name;
            settings[$"Rastro:Repositories:{i + 1}:Path"] = repos[i].Root;
        }
        return fixture.McpServices(settings, services => services.AddSingleton<ISolutionSource>(source));
    }

    public static ResolvedTarget TargetOf(IServiceScope scope, CrossRepo repo) =>
        scope.ServiceProvider.GetRequiredService<RepositoryCatalog>().Resolve(repo.Name, repo.Solution).Target!;

    public static Task<CrossAnalysis> AnalyzeAsync(IServiceScope scope, CrossRepo first, string entry, int depth, params CrossRepo[] cross) =>
        scope.ServiceProvider.GetRequiredService<CrossSystemAnalyzer>()
            .AnalyzeAsync(TargetOf(scope, first), entry, depth, cross.Select(c => c.Name).ToList(), CancellationToken.None);

    // Levantamento concluído no banco, como o worker deixa: grafo da costura mais os trechos.
    public static async Task<Survey> SaveSurveyAsync(IServiceScope scope, CrossRepo first, string entry, int depth, CrossAnalysis analysis, params CrossRepo[] cross)
    {
        var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
        var git = scope.ServiceProvider.GetRequiredService<IGitInfo>();
        var head = await git.HeadAsync(first.Root, CancellationToken.None);
        var survey = Survey.Create(first.Name, first.Solution, entry, depth, head?.Commit, head?.Branch, DateTimeOffset.UtcNow,
            cross.Select(c => c.Name).ToList());
        survey.Start(DateTimeOffset.UtcNow);
        survey.Complete(analysis.Graph, DateTimeOffset.UtcNow);
        db.Surveys.Add(survey);
        foreach (var (segment, index) in analysis.Segments.Select((s, i) => (s, i)))
            db.SurveySegments.Add(SurveySegment.Create(survey.Id, index + 1, segment.Repository, segment.SolutionPath, segment.EntryPoint, segment.CommitHash, segment.Branch));
        await db.SaveChangesAsync();
        return survey;
    }
}
