using System.Diagnostics;
using Rastro.Infrastructure.Processes;

namespace Rastro.Api.Desktop;

// Abre um link já aprovado pelo ExternalLinkPolicy no programa padrão do sistema.
public static class ExternalLinkOpener
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static void Open(string url, ILogger logger)
    {
        if (OperatingSystem.IsWindows())
        {
            // No Windows, abrir URL só funciona pelo ShellExecute (UseShellExecute = true), sem cmd.exe nem
            // linha de comando montada por nós. A proteção é o ExternalLinkPolicy, que já recusou tudo o que
            // não é host reconhecido ou esquema do editor.
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return;
        }

        // macOS e Linux: o programa de abrir com a URL como argumento separado, sem shell.
        var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        var result = ProcessRunner.RunAsync(opener, [url], null, Timeout, CancellationToken.None).GetAwaiter().GetResult();
        if (result.ExitCode != 0)
        {
            var (scheme, host) = ExternalLinkPolicy.Describe(url);
            logger.LogWarning("O sistema não abriu o link ({Scheme}, {Host}): {Opener} terminou com código {ExitCode}.", scheme, host, opener, result.ExitCode);
        }
    }
}
