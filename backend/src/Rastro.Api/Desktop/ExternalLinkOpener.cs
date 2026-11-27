using System.Diagnostics;
using Rastro.Infrastructure.Processes;

namespace Rastro.Api.Desktop;

// Abre um link já aprovado pelo ExternalLinkPolicy no programa padrão do sistema.
public static class ExternalLinkOpener
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // Devolve se o sistema abriu (a tela avisa quando não abriu).
    public static bool Open(string url, ILogger logger)
    {
        var (scheme, host) = ExternalLinkPolicy.Describe(url);
        if (OperatingSystem.IsWindows())
        {
            // No Windows, abrir URL só funciona pelo ShellExecute (UseShellExecute = true), sem cmd.exe nem
            // linha de comando montada por nós. A proteção é o ExternalLinkPolicy, que já recusou tudo o que
            // não é host reconhecido ou esquema do editor.
            try
            {
                using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // Esquema sem programa associado (o editor do link não está instalado, por exemplo).
                logger.LogWarning(ex, "O sistema não abriu o link ({Scheme}, {Host}).", scheme, host);
                return false;
            }
        }

        // macOS e Linux: o programa de abrir com a URL como argumento separado, sem shell.
        var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        var result = ProcessRunner.RunAsync(opener, [url], null, Timeout, CancellationToken.None).GetAwaiter().GetResult();
        if (result.ExitCode == 0) return true;
        logger.LogWarning("O sistema não abriu o link ({Scheme}, {Host}): {Opener} terminou com código {ExitCode}.", scheme, host, opener, result.ExitCode);
        return false;
    }
}
