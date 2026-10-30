using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rastro.Extractor;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Infrastructure.Analysis;

// Solution aberta e a pasta base dos caminhos relativos do grafo (a raiz do repositório,
// ou a cópia de trabalho quando a análise copia e restaura).
public sealed record SolutionHandle(Solution Solution, string BaseDirectory);

public interface ISolutionSource
{
    // fresh: ignora o cache (uma análise nova sempre lê o código atual).
    Task<SolutionHandle> OpenAsync(ResolvedTarget target, bool fresh, CancellationToken ct);

    // Fecha a solution e a tira do cache. A costura entre sistemas (Fase 25) solta cada solution
    // antes de abrir a próxima, para nunca haver duas na memória ao mesmo tempo.
    void Release(ResolvedTarget target);
}

// Erro com mensagem que pode ir para o usuário (sem caminho do disco nem stack).
public class AnalysisException(string message, Exception? inner = null) : Exception(message, inner);

// Abre solutions com o MSBuild e guarda as abertas por alguns minutos: um agente faz várias
// perguntas seguidas sobre a mesma solution, e cada carga de uma solution grande leva dezenas
// de segundos. Uma carga por vez (memória).
public sealed class MsBuildSolutionSource(IOptions<RastroOptions> options, ILogger<MsBuildSolutionSource> logger) : ISolutionSource, IDisposable
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", "node_modules", ".git", ".vs", ".angular", "dist", "TestResults" };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Entry(LoadedSolution Loaded, string BaseDirectory, string? WorkRoot, DateTimeOffset LoadedAt);

    public async Task<SolutionHandle> OpenAsync(ResolvedTarget target, bool fresh, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            EvictExpired();
            if (!fresh && _cache.TryGetValue(target.SolutionFullPath, out var cached))
                return new SolutionHandle(cached.Loaded.Solution, cached.BaseDirectory);

            if (_cache.Remove(target.SolutionFullPath, out var old)) Release(old);

            var entry = await LoadAsync(target, ct);
            _cache[target.SolutionFullPath] = entry;
            return new SolutionHandle(entry.Loaded.Solution, entry.BaseDirectory);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Release(ResolvedTarget target)
    {
        _gate.Wait();
        try
        {
            if (_cache.Remove(target.SolutionFullPath, out var entry)) Release(entry);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Entry> LoadAsync(ResolvedTarget target, CancellationToken ct)
    {
        var analysis = options.Value.Analysis;
        if (!analysis.CopyAndRestore)
            return new Entry(await LoadSolutionAsync(target.SolutionFullPath, ct), target.RepositoryRoot, null, DateTimeOffset.UtcNow);

        var workRoot = Path.Combine(analysis.WorkDirectory, Guid.NewGuid().ToString("N"));
        try
        {
            CopyTree(target.RepositoryRoot, workRoot);
            var solution = Path.Combine(workRoot, target.SolutionRelativePath);
            var restore = await ProcessRunner.RunAsync("dotnet", ["restore", solution], workRoot,
                TimeSpan.FromSeconds(analysis.RestoreTimeoutSeconds), ct);
            if (restore.ExitCode != 0)
            {
                logger.LogWarning("dotnet restore falhou com código {ExitCode}", restore.ExitCode);
                throw new AnalysisException("O dotnet restore da solution falhou. Confira se ela compila na máquina.");
            }

            return new Entry(await LoadSolutionAsync(solution, ct), workRoot, workRoot, DateTimeOffset.UtcNow);
        }
        catch
        {
            TryDelete(workRoot);
            throw;
        }
    }

    private async Task<LoadedSolution> LoadSolutionAsync(string path, CancellationToken ct)
    {
        var loaded = await SolutionLoader.LoadAsync(path, ct);
        if (loaded.Warnings.Count > 0)
            logger.LogWarning("Carga da solution com {WarningCount} aviso(s). Primeiro: {FirstWarning}", loaded.Warnings.Count, loaded.Warnings[0]);
        return loaded;
    }

    private void EvictExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _cache.Where(kv => now - kv.Value.LoadedAt > CacheTtl).Select(kv => kv.Key).ToList())
            if (_cache.Remove(key, out var entry)) Release(entry);
    }

    private void Release(Entry entry)
    {
        entry.Loaded.Dispose();
        if (entry.WorkRoot is not null) TryDelete(entry.WorkRoot);
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var folder in Directory.EnumerateDirectories(source))
            if (!SkippedFolders.Contains(Path.GetFileName(folder)))
                CopyTree(folder, Path.Combine(destination, Path.GetFileName(folder)));
    }

    private void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Pasta de trabalho da análise não foi apagada.");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Pasta de trabalho da análise não foi apagada.");
        }
    }

    public void Dispose()
    {
        foreach (var entry in _cache.Values) Release(entry);
        _cache.Clear();
        _gate.Dispose();
    }
}
