using System.Resources;
using Rastro.Core.Localization;

namespace Rastro.Ai;

// Mensagens do modo provedor que vão para a tela (ADR-009), em Resources/Messages.resx (neutro, inglês),
// Messages.pt-BR.resx e Messages.es.resx. Cultura sempre explícita, como no LocalizedText do núcleo.
// O prompt enviado ao modelo não passa por aqui: fica em inglês fixo (AiLanguage.Order).
internal static class AiText
{
    private static readonly ResourceManager Manager =
        new("Rastro.Ai.Resources.Messages", typeof(AiText).Assembly);

    public static string Get(string key, UiLanguage language, params object[] args)
    {
        var culture = language.Culture();
        var template = Manager.GetString(key, culture) ?? key;
        return args.Length == 0 ? template : string.Format(culture, template, args);
    }
}

// O idioma em que o modelo escreve o que volta para a tela. O prompt de sistema é sempre em inglês (o modelo
// segue melhor); só esta ordem muda com o idioma do pedido.
public static class AiLanguage
{
    public static string Name(UiLanguage language) => language switch
    {
        UiLanguage.Portuguese => "Portuguese (Brazil)",
        UiLanguage.Spanish => "Spanish",
        UiLanguage.English => "English",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Idioma desconhecido.")
    };

    public static string Order(UiLanguage language) => $"Write your answer in {Name(language)}.";
}
