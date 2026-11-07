namespace Rastro.Api.Desktop;

// Por que o "rastro" não abre a janela própria (ADR-007). None: abre.
public enum WindowSkipReason
{
    None = 0,
    // Dentro do WebApplicationFactory dos testes: o Program não é o executável.
    NotStandalone = 1,
    // Endereço vindo da configuração (dotnet run com o launchSettings, --urls, Docker): é desenvolvimento ou servidor.
    ConfiguredUrls = 2,
    // O usuário pediu só o console.
    NoWindowFlag = 3,
    // Sem a tela embutida (wwwroot) não há o que mostrar: no desenvolvimento a tela vem do ng serve.
    NoEmbeddedScreen = 4
}

// Decide se o "rastro" abre a janela própria. Os comandos da CLI (mcp, repo) nem chegam aqui: saem antes, no Program.
public static class DesktopLaunch
{
    public const string NoWindowFlag = "--sem-janela";

    // Tira o --sem-janela dos argumentos antes da configuração: sem valor, o leitor de linha de comando do .NET
    // tomaria o argumento seguinte (--porta) como valor dele.
    public static (bool NoWindow, string[] HostArgs) ExtractFlag(string[] args)
    {
        var hostArgs = args.Where(a => !string.Equals(a, NoWindowFlag, StringComparison.Ordinal)).ToArray();
        return (hostArgs.Length != args.Length, hostArgs);
    }

    public static WindowSkipReason Decide(bool standalone, bool configuredUrls, bool noWindow, bool hasEmbeddedScreen)
    {
        if (!standalone)
            return WindowSkipReason.NotStandalone;
        if (configuredUrls)
            return WindowSkipReason.ConfiguredUrls;
        if (noWindow)
            return WindowSkipReason.NoWindowFlag;
        return hasEmbeddedScreen ? WindowSkipReason.None : WindowSkipReason.NoEmbeddedScreen;
    }
}
