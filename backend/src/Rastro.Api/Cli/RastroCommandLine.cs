using Rastro.Core;
using Rastro.Infrastructure.Storage;
using Rastro.Mcp;

namespace Rastro.Api.Cli;

// Comandos do "rastro" (ADR-002). Sem comando (ou só opções, como --porta), sobe a tela: quem decide é o Program.
public static class RastroCommandLine
{
    public const int DefaultPort = 7450;

    public static bool IsCommand(string[] args) =>
        args is [var first, ..] && (!first.StartsWith('-') || first is "--version" or "--help" or "-h");

    public static async Task<int> RunAsync(string[] args)
    {
        var output = Console.Out;
        var error = Console.Error;
        return args switch
        {
            ["--version"] => Version(output),
            ["--help" or "-h" or "ajuda"] => Usage(output, 0),
            ["mcp", .. var rest] => await Mcp(rest),
            ["repo", .. var rest] => RepositoryCommand.Run(rest, RastroHome.ConfigPath, output, error),
            _ => Usage(error, 1)
        };
    }

    private static async Task<int> Mcp(string[] rest)
    {
        await McpHost.RunAsync(rest);
        return 0;
    }

    private static int Version(TextWriter output)
    {
        output.WriteLine($"rastro {RastroVersion.Current}");
        return 0;
    }

    private static int Usage(TextWriter writer, int exitCode)
    {
        writer.WriteLine($"rastro {RastroVersion.Current}: o rastro de cada funcionalidade no seu código .NET.");
        writer.WriteLine();
        writer.WriteLine("Uso:");
        writer.WriteLine($"  rastro [--porta N]            abre a tela em http://127.0.0.1:{DefaultPort} (só nesta máquina)");
        writer.WriteLine("  rastro repo add <nome> <pasta> [--build \"comando\"] [--testes \"comando\"]");
        writer.WriteLine("  rastro repo remove <nome>");
        writer.WriteLine("  rastro repo list");
        writer.WriteLine("  rastro mcp                     servidor MCP para o seu agente (ex.: Claude Code)");
        writer.WriteLine("  rastro --version");
        writer.WriteLine();
        writer.WriteLine($"Dados em {RastroHome.Folder} (troque com a variável {RastroHome.EnvironmentVariable}).");
        return exitCode;
    }
}
