using System.Resources;
using Rastro.Core.Localization;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Api.Cli;

// Texto do console (CLI, porta, janela) em Resources/Messages.resx (neutro, inglês), Messages.pt-BR.resx e
// Messages.es.resx (ADR-009). Cultura sempre explícita, como no LocalizedText do núcleo. Os nomes de comando e de
// opção (rastro repo add, --build, --testes, --sem-janela) são interface estável e nunca passam por aqui.
public static class ApiText
{
    private static readonly ResourceManager Manager =
        new("Rastro.Api.Resources.Messages", typeof(ApiText).Assembly);

    public static string Get(string key, UiLanguage language, params object[] args)
    {
        var culture = language.Culture();
        var template = Manager.GetString(key, culture) ?? key;
        return args.Length == 0 ? template : string.Format(culture, template, args);
    }

    // Idioma do console: o gravado no config.json e, sem ele, o do sistema operacional.
    public static UiLanguage CliLanguage(string configPath) =>
        new RepositoryConfigFile(configPath).GetLanguage() ?? UiLanguages.FromSystem();
}
