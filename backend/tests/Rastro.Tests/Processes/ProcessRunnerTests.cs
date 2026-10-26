using Rastro.Infrastructure.Processes;

namespace Rastro.Tests.Processes;

// Cenários PROC-NNN: tempo limite e cancelamento do ProcessRunner (Fase 13).
public class ProcessRunnerTests
{
    // Processo que demora: o comando de espera de cada sistema.
    private static (string File, string[] Args) Sleep() =>
        OperatingSystem.IsWindows() ? ("powershell", ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"]) : ("sleep", ["30"]);

    [Fact] // PROC-001
    public async Task Run_PassaDoTempo_LancaTimeout()
    {
        var (file, args) = Sleep();

        await Assert.ThrowsAsync<TimeoutException>(() => ProcessRunner.RunAsync(file, args, null, TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact] // PROC-002
    public async Task Run_QuemPediuDesiste_CancelaSemDizerQueFoiTempoLimite()
    {
        var (file, args) = Sleep();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessRunner.RunAsync(file, args, null, TimeSpan.FromMinutes(5), cts.Token));
    }
}
