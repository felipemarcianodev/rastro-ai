using Microsoft.Extensions.Configuration;
using Rastro.Api.Cli;
using Rastro.Api.Desktop;
using Rastro.Core.Localization;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Tests.Api;

// Cenários CLI-LANG-NNN: a CLI no idioma do config.json, com os nomes de comando e de opção estáveis (Fase 51, ADR-009).
public sealed class CliLanguageTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rastro-cli-lang-" + Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_folder, "home", "config.json");

    private string NewRepository(string name)
    {
        var path = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        return path;
    }

    private (int Code, string Output, string Error) Run(UiLanguage? language, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = RepositoryCommand.Run(args, ConfigPath, output, error, language);
        return (code, output.ToString(), error.ToString());
    }

    [Theory] // CLI-LANG-001
    [InlineData(UiLanguage.English, "opens Rastro in its own window")]
    [InlineData(UiLanguage.Portuguese, "abre o Rastro numa janela própria")]
    [InlineData(UiLanguage.Spanish, "abre Rastro en su propia ventana")]
    public void Ajuda_SaiNoIdioma_ComOsNomesDeComandoEstaveis(UiLanguage language, string expected)
    {
        var help = RastroCommandLine.Help(language);

        Assert.Contains(expected, help);
        // Interface estável: os nomes não mudam com o idioma.
        foreach (var name in new[] { "rastro [--porta N]", "rastro --sem-janela", "rastro repo add", "[--build", "[--testes", "rastro repo remove", "rastro repo list", "rastro mcp", "rastro --version" })
            Assert.Contains(name, help);
        // Os apelidos em inglês estão documentados nos três idiomas.
        Assert.Contains("--no-window", help);
        Assert.Contains("--tests", help);
        Assert.Contains("--port", help);
    }

    [Fact] // CLI-LANG-002
    public void RepoList_SemIdiomaPedido_SegueOConfigJson()
    {
        new RepositoryConfigFile(ConfigPath).SetLanguage("es");

        var spanish = Run(null, "list");
        new RepositoryConfigFile(ConfigPath).SetLanguage("en-US");
        var english = Run(null, "list");
        new RepositoryConfigFile(ConfigPath).SetLanguage("pt-BR");
        var portuguese = Run(null, "list");

        Assert.StartsWith("Ningún repositorio registrado", spanish.Output);
        Assert.StartsWith("No repository registered", english.Output);
        Assert.StartsWith("Nenhum repositório cadastrado", portuguese.Output);
    }

    [Fact] // CLI-LANG-003
    public void Repo_MensagensDeErroESucesso_NoIdiomaPedido()
    {
        var path = NewRepository("loja");

        var added = Run(UiLanguage.English, "add", "loja", path);
        var repeated = Run(UiLanguage.Spanish, "add", "loja", path);
        var badName = Run(UiLanguage.English, "add", "bad name", path);
        var removed = Run(UiLanguage.Spanish, "remove", "loja");
        var unknown = Run(UiLanguage.English, "remove", "loja");

        Assert.Equal(0, added.Code);
        Assert.StartsWith("Repository loja registered", added.Output);
        Assert.Contains("Ya existe un repositorio llamado loja", repeated.Error);
        Assert.Contains("rastro repo remove loja", repeated.Error);
        Assert.StartsWith("Invalid name", badName.Error);
        Assert.StartsWith("Repositorio loja eliminado", removed.Output);
        Assert.Equal("No repository named loja.\n", unknown.Error.Replace("\r\n", "\n"));
    }

    [Fact] // CLI-LANG-004
    public void Apelidos_EmIngles_ValemAoLadoDosNomesEmPortugues()
    {
        var path = NewRepository("loja");

        var added = Run(UiLanguage.English, "add", "loja", path, "--build", "dotnet build", "--tests", "dotnet test");

        Assert.Equal(0, added.Code);
        var options = new ConfigurationBuilder().AddJsonFile(ConfigPath).Build().GetSection(RastroOptions.Section).Get<RastroOptions>()!;
        var repo = Assert.Single(options.Repositories);
        Assert.Equal(("dotnet build", "dotnet test"), (repo.BuildCommand, repo.TestCommand));

        var (noWindow, hostArgs) = DesktopLaunch.ExtractFlag(["--no-window", "--port", "7460"]);
        Assert.True(noWindow);
        Assert.Equal(["--port", "7460"], hostArgs);
        Assert.True(DesktopLaunch.ExtractFlag(["--sem-janela"]).NoWindow);
    }

    [Fact] // CLI-LANG-005
    public void PortaOcupada_AvisoNoIdiomaPedido()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = PortProbe.Report(new PortCheck(PortState.OtherProgram), 7450, output, error, UiLanguage.Spanish);

        Assert.Equal(1, code);
        Assert.Contains("El puerto 7450 ya está en uso", error.ToString());
        Assert.Contains("--porta", error.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
