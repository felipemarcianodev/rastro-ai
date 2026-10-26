using System.Diagnostics;

namespace Rastro.Infrastructure.Processes;

public record ProcessResult(int ExitCode, string Output, string Error);

// Executa um processo externo (git, dotnet) com argumentos separados (sem shell) e timeout.
// environment: variáveis a mais para o processo, vindas só da configuração (nunca da requisição).
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory, TimeSpan timeout, CancellationToken ct,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var (name, value) in environment ?? new Dictionary<string, string>()) info.Environment[name] = value;

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Não foi possível iniciar {fileName}.");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        var output = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var error = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            // Quem pediu desistiu (requisição abortada, serviço parando): cancelamento, não tempo limite.
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException($"{fileName} passou de {timeout.TotalSeconds:0} s.");
        }

        return new ProcessResult(process.ExitCode, (await output).Trim(), (await error).Trim());
    }
}
