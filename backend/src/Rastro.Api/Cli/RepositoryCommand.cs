using Rastro.Infrastructure.Repositories;

namespace Rastro.Api.Cli;

// "rastro repo": cadastra no ~/.rastro/config.json os repositórios que o Rastro pode ler.
// A tela e o MCP só citam o nome; o caminho fica aqui (mesma regra do RepositoryCatalog).
// A leitura e a gravação do arquivo são do RepositoryConfigFile, o mesmo que a API usa.
public static class RepositoryCommand
{
    public static int Run(string[] args, string configPath, TextWriter output, TextWriter error)
    {
        var config = new RepositoryConfigFile(configPath);
        return args switch
        {
            ["add", var name, var folder, .. var rest] => Add(config, name, folder, rest, output, error),
            ["remove", var name] => Remove(config, name, output, error),
            ["list"] or [] => List(config, output),
            _ => Usage(error)
        };
    }

    private static int Add(RepositoryConfigFile config, string name, string folder, string[] rest, TextWriter output, TextWriter error)
    {
        var result = config.AddWithCommands(name, folder, Option(rest, "--build"), Option(rest, "--testes"));
        switch (result.Outcome)
        {
            case RepositoryConfigOutcome.InvalidName:
                error.WriteLine("Nome inválido: use letras, números, ponto, hífen ou sublinhado (até 100), começando por letra ou número.");
                return 1;
            case RepositoryConfigOutcome.FolderNotFound:
                error.WriteLine($"A pasta {result.Path} não existe.");
                return 1;
            case RepositoryConfigOutcome.Exists:
                error.WriteLine($"Já existe um repositório chamado {name}. Remova antes com: rastro repo remove {name}");
                return 1;
        }

        output.WriteLine($"Repositório {name} cadastrado: {result.Path}");
        if (!result.HasGit)
            output.WriteLine("Aviso: a pasta não tem .git; o levantamento fica sem o commit, e o aviso de código mudado não funciona.");
        return 0;
    }

    private static int Remove(RepositoryConfigFile config, string name, TextWriter output, TextWriter error)
    {
        if (config.Remove(name).Outcome == RepositoryConfigOutcome.NotFound)
        {
            error.WriteLine($"Nenhum repositório chamado {name}.");
            return 1;
        }

        output.WriteLine($"Repositório {name} removido. Os levantamentos dele continuam no banco.");
        return 0;
    }

    private static int List(RepositoryConfigFile config, TextWriter output)
    {
        var repositories = config.List();
        if (repositories.Count == 0)
        {
            output.WriteLine("Nenhum repositório cadastrado. Cadastre com: rastro repo add <nome> <pasta>");
            return 0;
        }

        foreach (var (name, path) in repositories)
            output.WriteLine($"{name}  {path}");
        return 0;
    }

    private static int Usage(TextWriter error)
    {
        error.WriteLine("Uso:");
        error.WriteLine("  rastro repo add <nome> <pasta> [--build \"comando\"] [--testes \"comando\"]");
        error.WriteLine("  rastro repo remove <nome>");
        error.WriteLine("  rastro repo list");
        return 1;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
