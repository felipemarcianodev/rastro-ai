using System.Globalization;
using System.Resources;

namespace Rastro.Core.Localization;

// Texto do servidor que vai para a tela (ADR-009). A cultura é sempre explícita: nada aqui lê a cultura da
// thread, para o mesmo pedido dar o mesmo texto em qualquer máquina. O neutro (Messages.resx) é o inglês.
public static class LocalizedText
{
    private static readonly ResourceManager Manager =
        new("Rastro.Core.Resources.Messages", typeof(LocalizedText).Assembly);

    public static string Get(string key, UiLanguage language, params object[] args) =>
        Get(key, language.Culture(), args);

    public static string Get(string key, CultureInfo culture, params object[] args)
    {
        var template = Manager.GetString(key, culture) ?? key;
        return args.Length == 0 ? template : string.Format(culture, template, args);
    }
}
