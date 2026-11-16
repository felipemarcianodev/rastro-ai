using Rastro.Core;
using Rastro.Core.Localization;
using Rastro.Infrastructure.Storage;
using Rastro.Mcp;

namespace Rastro.Api.Cli;

// Comandos do "rastro" (ADR-002). Sem comando (ou só opções, como --porta), sobe a tela: quem decide é o Program.
public static class RastroCommandLine
{
    public const int DefaultPort = 7450;

    public static bool IsCommand(string[] args) =>
        args is [var first, ..] && (!first.StartsWith('-') || first is "--version" or "--help" or "-h");

    // language nulo: o do config.json (e, sem ele, o do sistema). Os nomes de comando e de opção são os mesmos em
    // qualquer idioma; só a ajuda e as mensagens mudam.
    public static async Task<int> RunAsync(string[] args, UiLanguage? language = null)
    {
        var output = Console.Out;
        var error = Console.Error;
        var lang = language ?? ApiText.CliLanguage(RastroHome.ConfigPath);
        return args switch
        {
            ["--version"] => Version(output),
            ["--help" or "-h" or "ajuda"] => Usage(output, 0, lang),
            ["mcp", .. var rest] => await Mcp(rest),
            ["repo", .. var rest] => RepositoryCommand.Run(rest, RastroHome.ConfigPath, output, error, lang),
            _ => Usage(error, 1, lang)
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

    private static int Usage(TextWriter writer, int exitCode, UiLanguage lang)
    {
        writer.Write(Help(lang));
        return exitCode;
    }

    // A ajuda no idioma pedido. Os nomes de comando e de opção são os mesmos nos três.
    public static string Help(UiLanguage lang)
    {
        var help = new System.Text.StringBuilder();
        help.AppendLine(ApiText.Get("Cli.Usage.Title", lang, RastroVersion.Current));
        help.AppendLine();
        help.AppendLine(ApiText.Get("Cli.Usage.Header", lang));
        help.AppendLine($"  rastro [--porta N]            {ApiText.Get("Cli.Usage.Open", lang, DefaultPort)}");
        help.AppendLine($"  rastro --sem-janela           {ApiText.Get("Cli.Usage.NoWindow", lang)}");
        help.AppendLine("  " + ApiText.Get("Cli.Usage.RepoAdd", lang));
        help.AppendLine("  " + ApiText.Get("Cli.Usage.RepoRemove", lang));
        help.AppendLine("  rastro repo list");
        help.AppendLine($"  rastro mcp                     {ApiText.Get("Cli.Usage.Mcp", lang)}");
        help.AppendLine("  rastro --version");
        help.AppendLine();
        help.AppendLine(ApiText.Get("Cli.Usage.Aliases", lang));
        help.AppendLine(ApiText.Get("Cli.Usage.Data", lang, RastroHome.Folder, RastroHome.EnvironmentVariable));
        return help.ToString();
    }
}
