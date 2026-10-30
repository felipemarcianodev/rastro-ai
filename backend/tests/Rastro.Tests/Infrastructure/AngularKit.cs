using Microsoft.Extensions.Logging.Abstractions;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Processes;

namespace Rastro.Tests.Infrastructure;

// Node de mentira: o teste decide se o Angular está disponível sem depender da máquina.
internal sealed class FakeNodeRuntime(bool available) : INodeRuntime
{
    public bool Available { get; } = available;
}

// Repositório Angular temporário: cópia da fixture loja-web do extractor-ts (a mesma que o `node --test` usa),
// num repositório git. O Node roda de verdade; o TypeScript vem do extractor-ts/node_modules do repositório
// do Rastro (RASTRO_TYPESCRIPT), já que a fixture não tem node_modules próprio.
internal sealed class AngularRepo : IAsyncDisposable
{
    public const string TsConfig = "tsconfig.json";
    public const string Entry = "DetalhePedidoComponent.cancelar";

    public string Name { get; }
    public string Root { get; }

    static AngularRepo()
    {
        var typescript = Path.Combine(ExtractorFolder, "node_modules", "typescript");
        if (Directory.Exists(typescript)) Environment.SetEnvironmentVariable("RASTRO_TYPESCRIPT", typescript);
    }

    private AngularRepo(string name, string root)
    {
        Name = name;
        Root = root;
    }

    // O extractor-ts do repositório do Rastro (não a cópia da saída dos testes).
    public static string ExtractorFolder => Path.Combine(TestPaths.RepoRoot(), "extractor-ts");

    public static string ScriptPath => Path.Combine(ExtractorFolder, "extract.mjs");

    // O Node precisa existir: sem ele a suíte falha dizendo o que falta (o CI instala).
    public static void RequireNode()
    {
        Assert.True(new NodeRuntime(NullLogger<NodeRuntime>.Instance).Available,
            "O Node não está no PATH: os testes do extrator Angular precisam dele (instale o Node 20 ou mais novo).");
        Assert.True(Directory.Exists(Path.Combine(ExtractorFolder, "node_modules", "typescript")),
            "Falta o TypeScript do extrator: rode `npm ci` em extractor-ts.");
    }

    public static async Task<AngularRepo> CreateAsync(string prefix = "loja", bool git = true)
    {
        var name = $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 7)];
        var root = Path.Combine(Path.GetTempPath(), "rastro-tests", "angular-" + Guid.NewGuid().ToString("N"), name);
        CopyFolder(Path.Combine(ExtractorFolder, "test", "fixtures", "loja-web"), root);

        var repo = new AngularRepo(name, root);
        if (git)
        {
            await repo.GitAsync("init", "-q", "-b", "main");
            await repo.GitAsync("config", "user.email", "teste@rastro.local");
            await repo.GitAsync("config", "user.name", "Teste");
            await repo.GitAsync("add", ".");
            await repo.GitAsync("commit", "-q", "-m", "inicial");
        }
        return repo;
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var folder in Directory.GetDirectories(from))
            if (!string.Equals(Path.GetFileName(folder), "node_modules", StringComparison.OrdinalIgnoreCase))
                CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
    }

    private async Task GitAsync(params string[] args)
    {
        var result = await ProcessRunner.RunAsync("git", ["-C", Root, .. args], null, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', args)} falhou: {result.Error}");
    }

    public ValueTask DisposeAsync()
    {
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
