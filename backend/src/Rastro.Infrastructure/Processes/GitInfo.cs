using Microsoft.Extensions.Logging;
namespace Rastro.Infrastructure.Processes;

public record GitHead(string Commit, string? Branch);

public interface IGitInfo
{
    // Null quando a pasta não é um repositório git (ou o git não está instalado).
    Task<GitHead?> HeadAsync(string repositoryRoot, CancellationToken ct);

    // URL do remote "origin" (Fase 56), para o link do código no host. Nulo sem remote ou sem git. Pode trazer
    // usuário e token: quem usa tira a credencial (CodeLinks.ParseRemote) e nunca grava o valor no log.
    Task<string?> OriginUrlAsync(string repositoryRoot, CancellationToken ct);
}

public class GitCli(ILogger<GitCli> logger) : IGitInfo
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task<GitHead?> HeadAsync(string repositoryRoot, CancellationToken ct)
    {
        try
        {
            var commit = await ProcessRunner.RunAsync("git", ["-C", repositoryRoot, "rev-parse", "HEAD"], null, Timeout, ct);
            if (commit.ExitCode != 0)
            {
                logger.LogInformation("Pasta sem git ou sem commit: {ExitCode}", commit.ExitCode);
                return null;
            }

            var branch = await ProcessRunner.RunAsync("git", ["-C", repositoryRoot, "rev-parse", "--abbrev-ref", "HEAD"], null, Timeout, ct);
            return new GitHead(commit.Output, branch.ExitCode == 0 && branch.Output != "HEAD" ? branch.Output : null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or TimeoutException)
        {
            // Git ausente ou travado: o levantamento segue sem commit (fica sem como saber se envelheceu).
            logger.LogWarning(ex, "Não foi possível ler o commit do repositório.");
            return null;
        }
    }

    public async Task<string?> OriginUrlAsync(string repositoryRoot, CancellationToken ct)
    {
        try
        {
            var remote = await ProcessRunner.RunAsync("git", ["-C", repositoryRoot, "remote", "get-url", "origin"], null, Timeout, ct);
            if (remote.ExitCode == 0 && !string.IsNullOrWhiteSpace(remote.Output)) return remote.Output.Trim();
            logger.LogInformation("Repositório sem remote origin: {ExitCode}", remote.ExitCode);
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or TimeoutException)
        {
            // Git ausente ou travado: o código fica sem link para o host, só o do editor.
            logger.LogWarning(ex, "Não foi possível ler o remote do repositório.");
            return null;
        }
    }
}
