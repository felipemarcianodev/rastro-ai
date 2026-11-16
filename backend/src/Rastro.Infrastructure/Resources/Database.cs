using System.Globalization;
using System.Resources;
using Rastro.Core.Localization;

namespace Rastro.Infrastructure.Resources;

// Textos do banco vivo e do esquema que vão para a tela (ADR-009), em Database.resx (neutro, inglês), Database.pt-BR.resx
// e Database.es.resx. A cultura é sempre explícita: nada aqui lê a cultura da thread, para a mesma consulta dar
// os mesmos achados em qualquer máquina.
public static class Database
{
    private static readonly ResourceManager Manager =
        new("Rastro.Infrastructure.Resources.Database", typeof(Database).Assembly);

    public static string Get(string key, UiLanguage language, params object[] args) =>
        Get(key, language.Culture(), args);

    public static string Get(string key, CultureInfo culture, params object[] args)
    {
        var template = Manager.GetString(key, culture) ?? key;
        return args.Length == 0 ? template : string.Format(culture, template, args);
    }
}
