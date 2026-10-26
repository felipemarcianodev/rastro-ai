using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rastro.Core.Validation;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Validation;

namespace Rastro.Tests.Processes;

// Cenários VAL-CMD-NNN: build e testes da validação rodando na própria máquina (ADR-002), no lugar do executor no Windows.
public sealed class LocalCommandRunnerTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("rastro-cmd-").FullName;

    private static LocalCommandRunner Runner() =>
        new(Options.Create(new RastroOptions { Analysis = new AnalysisOptions { ValidationCommandTimeoutSeconds = 60 } }), NullLogger<LocalCommandRunner>.Instance);

    [Fact] // VAL-CMD-001
    public async Task Build_ComandoDaConfiguracao_Passa()
    {
        var repo = new RepositoryOptions { Name = "fixture", Path = _repo, BuildCommand = "dotnet --version" };

        var check = await Runner().RunAsync("Build", repo, ValidationCommand.Build, _repo, CancellationToken.None);

        Assert.Equal(CheckStatus.Passed, check.Status);
    }

    [Fact] // VAL-CMD-002
    public async Task Falha_MostraASaida_EVariaveisDaConfiguracaoChegamAoProcesso()
    {
        var repo = new RepositoryOptions
        {
            Name = "fixture",
            Path = _repo,
            TestCommand = "git rev-parse --git-dir",
            Environment = { ["GIT_DIR"] = "pasta-que-nao-existe-rastro" }
        };

        var check = await Runner().RunAsync("Testes", repo, ValidationCommand.Test, _repo, CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.Contains("pasta-que-nao-existe-rastro", check.Detail);
    }

    [Fact] // VAL-CMD-003
    public async Task SemComando_FicaComoNaoConfigurado()
    {
        var repo = new RepositoryOptions { Name = "fixture", Path = _repo };

        var check = await Runner().RunAsync("Testes", repo, ValidationCommand.Test, _repo, CancellationToken.None);

        Assert.Equal(CheckStatus.Skipped, check.Status);
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);
}
