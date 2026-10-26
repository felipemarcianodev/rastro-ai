using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rastro.Core.Validation;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Processes;

namespace Rastro.Infrastructure.Validation;

// Qual comando da configuração do repositório a validação roda.
public enum ValidationCommand
{
    Build = 0,
    Test = 1
}

// Quem roda o build e os testes da validação: o próprio processo do Rastro, na máquina do usuário
// (ADR-002). A linha de comando vem só da configuração do repositório, sem shell e com tempo limite.
public interface IValidationCommandRunner
{
    Task<ValidationCheck> RunAsync(string description, RepositoryOptions repository, ValidationCommand command, string workingDirectory, CancellationToken ct);
}

public static class ValidationOutput
{
    public const int MaxLength = 1500;

    public static string Tail(string text) => text.Length <= MaxLength ? text : "…" + text[^MaxLength..];
}

public class LocalCommandRunner(IOptions<RastroOptions> options, ILogger<LocalCommandRunner> logger) : IValidationCommandRunner
{
    public async Task<ValidationCheck> RunAsync(string description, RepositoryOptions repository, ValidationCommand command, string workingDirectory, CancellationToken ct)
    {
        var commandLine = command == ValidationCommand.Build ? repository.BuildCommand : repository.TestCommand;
        var parts = (commandLine ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return new ValidationCheck(description, CheckStatus.Skipped, "Comando não configurado para este repositório.");

        var timeout = TimeSpan.FromSeconds(options.Value.Analysis.ValidationCommandTimeoutSeconds);
        try
        {
            var run = await ProcessRunner.RunAsync(parts[0], parts.Skip(1), workingDirectory, timeout, ct, repository.Environment);
            if (run.ExitCode == 0) return new ValidationCheck(description, CheckStatus.Passed);

            logger.LogInformation("{Description} terminou com código {ExitCode}.", description, run.ExitCode);
            return new ValidationCheck(description, CheckStatus.Failed, ValidationOutput.Tail($"{run.Output}\n{run.Error}".Trim()));
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "{Description} passou do tempo limite.", description);
            return new ValidationCheck(description, CheckStatus.Failed, $"Passou de {timeout.TotalMinutes:0} min e foi interrompido.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            logger.LogWarning(ex, "{Description}: executável não encontrado.", description);
            return new ValidationCheck(description, CheckStatus.Failed, $"Não foi possível executar \"{parts[0]}\".");
        }
    }
}
