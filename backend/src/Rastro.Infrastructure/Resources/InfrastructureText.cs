using System.Resources;
using Rastro.Core.Localization;

namespace Rastro.Infrastructure.Resources;

// Texto da costura entre sistemas que o levantamento grava no grafo (ADR-009), em Messages.resx (neutro, inglês),
// Messages.pt-BR.resx e Messages.es.resx. Cultura sempre explícita, como no LocalizedText do núcleo.
internal static class InfrastructureText
{
    private static readonly ResourceManager Manager =
        new("Rastro.Infrastructure.Resources.Messages", typeof(InfrastructureText).Assembly);

    public static string Get(string key, UiLanguage language, params object[] args)
    {
        var culture = language.Culture();
        var template = Manager.GetString(key, culture) ?? key;
        return args.Length == 0 ? template : string.Format(culture, template, args);
    }
}
