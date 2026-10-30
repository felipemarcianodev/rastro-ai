using Microsoft.Extensions.Options;
using Rastro.Core.Graph;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Configuration;

namespace Rastro.Infrastructure.Repositories;

// Language: .NET (solution ou projeto) ou Angular (tsconfig); sai do nome do arquivo.
public record ResolvedTarget(string RepositoryName, string RepositoryRoot, string SolutionFullPath, string SolutionRelativePath, SourceLanguage Language = SourceLanguage.DotNet);

public enum CatalogError
{
    RepositoryNotAllowed = 0,
    SolutionPathInvalid = 1,
    SolutionNotFound = 2
}

public record CatalogResult(ResolvedTarget? Target, CatalogError? Error)
{
    public static CatalogResult Ok(ResolvedTarget target) => new(target, null);
    public static CatalogResult Fail(CatalogError error) => new(null, error);
}

// Única porta para caminhos no disco: só repositórios da configuração, e a solution
// precisa ficar dentro da raiz do repositório depois de canonicalizada (sem "..", sem caminho absoluto).
// IOptionsMonitor: o "rastro repo add" grava o config.json e a lista muda sem reiniciar.
// Aceita também tsconfig.json e tsconfig.*.json, o ponto de partida do levantamento Angular (Fase 26).
public class RepositoryCatalog(IOptionsMonitor<RastroOptions> options, INodeRuntime node)
{
    private static readonly string[] SolutionExtensions = [".sln", ".slnx", ".csproj"];
    private static readonly string[] SourceExtensions = [".cs", ".ts", ".html"];
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", "node_modules", ".git", ".vs", ".angular", "dist", "packages" };

    // Entrada sem nome ou sem caminho é ignorada (no Docker, os repositórios opcionais vêm vazios).
    private IEnumerable<RepositoryOptions> Configured =>
        options.CurrentValue.Repositories.Where(r => !string.IsNullOrWhiteSpace(r.Name) && !string.IsNullOrWhiteSpace(r.Path));

    public IReadOnlyList<string> Names() =>
        Configured.Select(r => r.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public string? RootOf(string repositoryName) =>
        Configured
            .FirstOrDefault(r => string.Equals(r.Name, repositoryName, StringComparison.OrdinalIgnoreCase))
            is { } repo ? Path.GetFullPath(repo.Path) : null;

    public CatalogResult Resolve(string repositoryName, string solutionPath)
    {
        var root = RootOf(repositoryName);
        if (root is null) return CatalogResult.Fail(CatalogError.RepositoryNotAllowed);

        if (string.IsNullOrWhiteSpace(solutionPath) || Path.IsPathRooted(solutionPath)
            || !(IsSolutionFile(solutionPath) || IsTsConfig(solutionPath)))
            return CatalogResult.Fail(CatalogError.SolutionPathInvalid);

        var full = Path.GetFullPath(Path.Combine(root, solutionPath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            return CatalogResult.Fail(CatalogError.SolutionPathInvalid);

        if (!File.Exists(full)) return CatalogResult.Fail(CatalogError.SolutionNotFound);

        var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        return CatalogResult.Ok(new ResolvedTarget(repositoryName, root, full, relative, SourceLanguages.FromPath(relative)));
    }

    // Arquivo de código dentro do repositório. Null se o caminho sair da raiz, for absoluto ou não for .cs, .ts ou .html.
    public string? SourceFile(string repositoryName, string relativePath)
    {
        var root = RootOf(repositoryName);
        if (root is null || string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)
            || !SourceExtensions.Contains(Path.GetExtension(relativePath), StringComparer.OrdinalIgnoreCase))
            return null;

        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    // Solutions do repositório (até 4 níveis), para a tela sugerir. Os .csproj só entram quando não há
    // nenhuma .sln nem .slnx: no meio de dezenas de projetos a solution se perdia (o levantamento ainda
    // aceita .csproj, pelo Resolve). Com o Node disponível, entra também o tsconfig de cada pasta com
    // angular.json (o tsconfig.app.json, ou o tsconfig.json se não houver).
    public IReadOnlyList<string>? Solutions(string repositoryName)
    {
        var root = RootOf(repositoryName);
        if (root is null || !Directory.Exists(root)) return null;

        var found = new List<string>();
        Collect(root, root, 0, found);
        var solutions = found.Where(p => !IsProject(p)).ToList();
        return (solutions.Count > 0 ? solutions : found)
            .OrderBy(p => p.Count(c => c == '/'))
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Só as solutions e projetos .NET: a costura e o MCP abrem o repositório pelo Roslyn e não servem de tsconfig.
    public IReadOnlyList<string>? DotNetSolutions(string repositoryName) =>
        Solutions(repositoryName)?.Where(p => SourceLanguages.FromPath(p) == SourceLanguage.DotNet).ToList();

    private static bool IsSolutionFile(string path) =>
        SolutionExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // tsconfig.json ou tsconfig.<nome>.json.
    private static bool IsTsConfig(string path) =>
        SourceLanguages.FromPath(path) == SourceLanguage.Angular;

    private static bool IsProject(string path) =>
        Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase);

    private void Collect(string root, string folder, int depth, List<string> found)
    {
        if (depth > 4) return;

        foreach (var file in Directory.EnumerateFiles(folder))
            if (SolutionExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                found.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));

        if (node.Available && File.Exists(Path.Combine(folder, "angular.json")))
        {
            var tsconfig = new[] { "tsconfig.app.json", "tsconfig.json" }.FirstOrDefault(n => File.Exists(Path.Combine(folder, n)));
            if (tsconfig is not null)
                found.Add(Path.GetRelativePath(root, Path.Combine(folder, tsconfig)).Replace('\\', '/'));
        }

        foreach (var sub in Directory.EnumerateDirectories(folder))
            if (!SkippedFolders.Contains(Path.GetFileName(sub)))
                Collect(root, sub, depth + 1, found);
    }
}
