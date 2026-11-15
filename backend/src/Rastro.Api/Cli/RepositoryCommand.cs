using Rastro.Core.Localization;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Api.Cli;

// "rastro repo": cadastra no ~/.rastro/config.json os repositórios que o Rastro pode ler.
// A tela e o MCP só citam o nome; o caminho fica aqui (mesma regra do RepositoryCatalog).
// A leitura e a gravação do arquivo são do RepositoryConfigFile, o mesmo que a API usa.
public static class RepositoryCommand
{
    // language nulo: o do config.json (e, sem ele, o do sistema). Os nomes dos comandos e das opções não mudam com o idioma.
    public static int Run(string[] args, string configPath, TextWriter output, TextWriter error, UiLanguage? language = null)
    {
        var config = new RepositoryConfigFile(configPath);
        var lang = language ?? config.GetLanguage() ?? UiLanguages.FromSystem();
        return args switch
        {
            ["add", var name, var folder, .. var rest] => Add(config, name, folder, rest, output, error, lang),
            ["remove", var name] => Remove(config, name, output, error, lang),
            ["list"] or [] => List(config, output, lang),
            _ => Usage(error, lang)
        };
    }

    private static int Add(RepositoryConfigFile config, string name, string folder, string[] rest, TextWriter output, TextWriter error, UiLanguage lang)
    {
        var result = config.AddWithCommands(name, folder, Option(rest, "--build"), Option(rest, "--testes", "--tests"));
        switch (result.Outcome)
        {
            case RepositoryConfigOutcome.InvalidName:
                error.WriteLine(ApiText.Get("Cli.Repo.InvalidName", lang));
                return 1;
            case RepositoryConfigOutcome.FolderNotFound:
                error.WriteLine(ApiText.Get("Cli.Repo.FolderNotFound", lang, result.Path ?? string.Empty));
                return 1;
            case RepositoryConfigOutcome.Exists:
                error.WriteLine(ApiText.Get("Cli.Repo.Exists", lang, name));
                return 1;
        }

        output.WriteLine(ApiText.Get("Cli.Repo.Added", lang, name, result.Path ?? string.Empty));
        if (!result.HasGit)
            output.WriteLine(ApiText.Get("Cli.Repo.NoGit", lang));
        return 0;
    }

    private static int Remove(RepositoryConfigFile config, string name, TextWriter output, TextWriter error, UiLanguage lang)
    {
        if (config.Remove(name).Outcome == RepositoryConfigOutcome.NotFound)
        {
            error.WriteLine(ApiText.Get("Cli.Repo.NotFound", lang, name));
            return 1;
        }

        output.WriteLine(ApiText.Get("Cli.Repo.Removed", lang, name));
        return 0;
    }

    private static int List(RepositoryConfigFile config, TextWriter output, UiLanguage lang)
    {
        var repositories = config.List();
        if (repositories.Count == 0)
        {
            output.WriteLine(ApiText.Get("Cli.Repo.ListEmpty", lang));
            return 0;
        }

        foreach (var (name, path) in repositories)
            output.WriteLine($"{name}  {path}");
        return 0;
    }

    private static int Usage(TextWriter error, UiLanguage lang)
    {
        error.WriteLine(ApiText.Get("Cli.Usage.Header", lang));
        error.WriteLine("  " + ApiText.Get("Cli.Usage.RepoAdd", lang));
        error.WriteLine("  " + ApiText.Get("Cli.Usage.RepoRemove", lang));
        error.WriteLine("  rastro repo list");
        return 1;
    }

    // Aceita o nome estável em português e o apelido em inglês (--testes e --tests).
    private static string? Option(string[] args, params string[] names)
    {
        var index = Array.FindIndex(args, a => names.Contains(a));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
