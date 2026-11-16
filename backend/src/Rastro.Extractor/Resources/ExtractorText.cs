using System.Globalization;
using System.Resources;
using Rastro.Core.Localization;

namespace Rastro.Extractor.Resources;

// Texto dos achados de qualidade e de acesso ao banco que o levantamento grava no grafo (ADR-009), em
// Messages.resx (neutro, inglês), Messages.pt-BR.resx e Messages.es.resx. A cultura é sempre explícita: nada aqui
// lê a cultura da thread, para o mesmo código dar os mesmos achados em qualquer máquina.
internal static class ExtractorText
{
    private static readonly ResourceManager Manager =
        new("Rastro.Extractor.Resources.Messages", typeof(ExtractorText).Assembly);

    public static string Get(string key, UiLanguage language, params object[] args)
    {
        var culture = language.Culture();
        var template = Manager.GetString(key, culture) ?? key;
        return args.Length == 0 ? template : string.Format(culture, template, args);
    }
}
