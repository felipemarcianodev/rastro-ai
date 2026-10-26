using Microsoft.Extensions.Logging;
namespace Rastro.Infrastructure.Processes;

public record GitHead(string Commit, string? Branch);

public interface IGitInfo
{
    // Null quando a pasta não é um repositório git (ou o git não está instalado).
    Task<GitHead?> HeadAsync(string repositoryRoot, CancellationToken ct);
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
}
