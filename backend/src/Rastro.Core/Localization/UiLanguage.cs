using System.Globalization;

namespace Rastro.Core.Localization;

// Idiomas do Rastro (ADR-009). Os valores são os mesmos do enum do frontend e viajam como int no JSON.
public enum UiLanguage
{
    English = 0,
    Portuguese = 1,
    Spanish = 2
}

public static class UiLanguages
{
    public const string EnglishCode = "en-US";
    public const string PortugueseCode = "pt-BR";
    public const string SpanishCode = "es";

    public static IReadOnlyList<UiLanguage> All { get; } = [UiLanguage.English, UiLanguage.Portuguese, UiLanguage.Spanish];

    public static string Code(this UiLanguage language) => language switch
    {
        UiLanguage.English => EnglishCode,
        UiLanguage.Portuguese => PortugueseCode,
        UiLanguage.Spanish => SpanishCode,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Idioma desconhecido.")
    };

    public static CultureInfo Culture(this UiLanguage language) => CultureInfo.GetCultureInfo(language.Code());

    public static bool IsDefined(int value) => Enum.IsDefined(typeof(UiLanguage), value);

    // Aceita o código completo ou só o prefixo do idioma: "pt-PT" vira Portuguese e "es-MX" vira Spanish.
    public static UiLanguage? FromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var prefix = code.Trim().Split('-', '_')[0];
        return prefix.ToLowerInvariant() switch
        {
            "en" => UiLanguage.English,
            "pt" => UiLanguage.Portuguese,
            "es" => UiLanguage.Spanish,
            _ => null
        };
    }

    public static UiLanguage FromCulture(CultureInfo culture) => FromCode(culture.Name) ?? UiLanguage.English;

    // Idioma do sistema operacional; inglês quando não é um dos três. Lê a cultura instalada, e não a
    // da thread: dentro de um pedido, a da thread já é a do Accept-Language.
    public static UiLanguage FromSystem(CultureInfo? culture = null) =>
        FromCulture(culture ?? CultureInfo.InstalledUICulture);
}
