using System.Resources;
using System.Text.Json;
using Rastro.Core.Localization;

// Texto desta ferramenta de desenvolvimento em Resources/Messages.resx (neutro, inglês), Messages.pt-BR.resx e
// Messages.es.resx (ADR-009). Os nomes de comando e de opção não mudam com o idioma.
internal static class CliText
{
    private static readonly ResourceManager Manager = new("Rastro.Cli.Resources.Messages", typeof(CliText).Assembly);

    public static string Get(string key, UiLanguage language, params object[] args)
    {
        var culture = language.Culture();
        var template = Manager.GetString(key, culture) ?? key;
        return args.Length == 0 ? template : string.Format(culture, template, args);
    }

    // Idioma gravado no config.json da pasta do Rastro (RASTRO_HOME ou ~/.rastro) e, sem ele, o do sistema.
    public static UiLanguage Language()
    {
        try
        {
            var custom = Environment.GetEnvironmentVariable("RASTRO_HOME");
            var folder = custom is { Length: > 0 }
                ? Path.GetFullPath(custom)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rastro");
            var path = Path.Combine(folder, "config.json");
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("Rastro", out var rastro)
                    && rastro.TryGetProperty("Language", out var code)
                    && UiLanguages.FromCode(code.GetString()) is { } saved)
                    return saved;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            // config.json ilegível ou fora do formato: esta ferramenta só escolhe o idioma do texto, então segue o do sistema.
            Console.Error.WriteLine($"rastro: config.json ilegível, usando o idioma do sistema ({ex.GetType().Name}).");
        }
        return UiLanguages.FromSystem();
    }
}
