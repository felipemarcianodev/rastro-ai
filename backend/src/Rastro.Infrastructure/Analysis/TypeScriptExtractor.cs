using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rastro.Core.Graph;
using Rastro.Extractor;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Infrastructure.Analysis;

public interface ITypeScriptExtractor
{
    Task<ExtractionResult> ExtractAsync(ResolvedTarget target, string entryPoint, int maxDepth, CancellationToken ct);
}

// Extrator Angular (ADR-005): roda o script Node (extractor-ts/extract.mjs) pelo ProcessRunner, sem shell e com
// timeout, e lê o JSON da saída padrão. O script usa o compilador do TypeScript do repositório analisado.
// scriptPath: só os testes trocam (apontam para o script do repositório); em produção o script fica ao lado do executável.
public sealed class TypeScriptExtractor(
    IOptions<RastroOptions> options,
    ILogger<TypeScriptExtractor> logger,
    string? scriptPath = null) : ITypeScriptExtractor
{
    private sealed record ScriptHttpCall(string NodeId, HttpCall Call, int Depth);

    private sealed record ScriptOutput(FlowGraph? Graph, List<ScriptHttpCall>? HttpCalls, string? Error);

    public async Task<ExtractionResult> ExtractAsync(ResolvedTarget target, string entryPoint, int maxDepth, CancellationToken ct)
    {
        var script = scriptPath ?? Path.Combine(AppContext.BaseDirectory, "extractor-ts", "extract.mjs");
        if (!File.Exists(script))
        {
            logger.LogError("Script do extrator Angular ausente em {Script}.", script);
            throw new AnalysisException("O extrator Angular não veio no pacote desta instalação. Reinstale o Rastro.");
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.Analysis.TypeScriptTimeoutSeconds));
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(NodeCommand.FileName,
                [script, "--root", target.RepositoryRoot, "--project", target.SolutionRelativePath, "--entry", entryPoint, "--depth", maxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                target.RepositoryRoot, timeout, ct);
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "Extrator Angular passou do tempo no repositório {Repository}.", target.RepositoryName);
            throw new AnalysisException($"A leitura do Angular passou de {timeout.TotalSeconds:0} s. Aumente Rastro:Analysis:TypeScriptTimeoutSeconds ou reduza a profundidade.", ex);
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "Node não pôde ser iniciado para o repositório {Repository}.", target.RepositoryName);
            throw new AnalysisException("O Node não foi encontrado nesta máquina. Instale o Node para levantar fluxos Angular.", ex);
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
            logger.LogDebug("Saída de erro do extrator Angular: {Error}", result.Error);

        ScriptOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<ScriptOutput>(LastLine(result.Output), FlowGraphJson.Options);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Saída do extrator Angular não é JSON (repositório {Repository}, código {ExitCode}): {Error}",
                target.RepositoryName, result.ExitCode, result.Error);
            throw new AnalysisException("Não foi possível ler o código Angular. Veja o log da API para o detalhe.", ex);
        }

        if (output?.Error is { Length: > 0 } message)
            throw new AnalysisException(message);

        if (result.ExitCode != 0 || output?.Graph is null)
        {
            logger.LogError("Extrator Angular falhou sem mensagem (repositório {Repository}, código {ExitCode}): {Error}",
                target.RepositoryName, result.ExitCode, result.Error);
            throw new AnalysisException("Não foi possível ler o código Angular. Veja o log da API para o detalhe.");
        }

        var calls = (output.HttpCalls ?? []).Select(c => new HttpCallSite(c.NodeId, c.Call, c.Depth)).ToList();
        logger.LogInformation("Angular extraído de {Repository}: {NodeCount} nós, {CallCount} chamada(s) HTTP.",
            target.RepositoryName, output.Graph.Nodes.Count, calls.Count);
        return new ExtractionResult(output.Graph, calls);
    }

    // O script escreve uma linha só; se algum aviso do Node escapar para a saída padrão, vale a última.
    private static string LastLine(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "{}" : lines[^1];
    }
}
