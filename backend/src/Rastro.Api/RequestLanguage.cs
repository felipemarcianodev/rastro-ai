using System.Globalization;
using Rastro.Core.Localization;

namespace Rastro.Api;

// Idioma do pedido em curso (ADR-009). A borda da API é o único lugar que lê a cultura da thread: o
// RequestLocalization já resolveu Accept-Language, config.json e sistema. O núcleo recebe o idioma por parâmetro.
public static class RequestLanguage
{
    public static UiLanguage Current => UiLanguages.FromCulture(CultureInfo.CurrentUICulture);
}
