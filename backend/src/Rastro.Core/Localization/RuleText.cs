using System.Resources;

namespace Rastro.Core.Localization;

// Texto das regras do fluxo (FlowRuleExtractor), em Resources/Rules.resx (neutro, inglês), Rules.pt-BR.resx e
// Rules.es.resx. Conjunto próprio, separado do Messages. Cultura sempre explícita, como no LocalizedText.
internal static class RuleText
{
    private static readonly ResourceManager Manager =
        new("Rastro.Core.Resources.Rules", typeof(RuleText).Assembly);

    public static string Get(string key, UiLanguage language, params object[] args)
    {
        var culture = language.Culture();
        var template = Manager.GetString(key, culture) ?? key;
        return args.Length == 0 ? template : string.Format(culture, template, args);
    }
}
