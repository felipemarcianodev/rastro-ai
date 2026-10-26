using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Rastro.Extractor;

// Solution carregada com o MSBuild (tipos resolvidos). Mantém o workspace vivo enquanto
// a solution for usada. Quem hospeda precisa registrar o MSBuildLocator antes (CLI, API).
public sealed class LoadedSolution(MSBuildWorkspace workspace, Solution solution, IReadOnlyList<string> warnings) : IDisposable
{
    public Solution Solution { get; } = solution;

    // Falhas não fatais da carga (projeto que não restaurou, referência ausente).
    public IReadOnlyList<string> Warnings { get; } = warnings;

    public void Dispose() => workspace.Dispose();
}

public static class SolutionLoader
{
    public static async Task<LoadedSolution> LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Solution ou projeto não encontrado: {path}", path);

        var workspace = MSBuildWorkspace.Create();
        var warnings = new List<string>();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            // Auditoria de pacotes do NuGet (vulnerabilidade conhecida) não afeta a análise.
            if (!IsPackageAudit(e.Diagnostic.Message))
                warnings.Add($"{e.Diagnostic.Kind}: {e.Diagnostic.Message}");
        });

        try
        {
            var solution = Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                ? (await workspace.OpenProjectAsync(path, cancellationToken: ct)).Solution
                : await workspace.OpenSolutionAsync(path, cancellationToken: ct);

            return new LoadedSolution(workspace, solution, warnings);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    internal static bool IsPackageAudit(string message) =>
        message.Contains("severity vulnerability", StringComparison.OrdinalIgnoreCase)
        || message.Contains("NU190", StringComparison.Ordinal);
}
