using Microsoft.Extensions.Configuration;

namespace Rastro.Infrastructure.Storage;

// Pasta do Rastro na máquina do usuário (ADR-002): banco e repositórios cadastrados.
// RASTRO_HOME troca a pasta (testes e quem quiser os dados em outro lugar).
public static class RastroHome
{
    public const string EnvironmentVariable = "RASTRO_HOME";

    public static string Folder =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rastro");

    public static string DatabasePath => Path.Combine(Folder, "rastro.db");

    // Mesmo formato do appsettings ({ "Rastro": { "Repositories": [...] } }); quem grava é o "rastro repo".
    public static string ConfigPath => Path.Combine(Folder, "config.json");

    public static string EnsureFolder()
    {
        Directory.CreateDirectory(Folder);
        return Folder;
    }

    // Entra por último: o que o usuário cadastrou vale sobre o appsettings. Recarrega ao mudar,
    // então um "rastro repo add" aparece na tela sem reiniciar.
    public static IConfigurationBuilder AddRastroHome(this IConfigurationBuilder configuration) =>
        configuration.AddJsonFile(ConfigPath, optional: true, reloadOnChange: true);
}
