using System.Globalization;
using System.Resources;
using System.Text.Json;
using Rastro.Core.Localization;

namespace Rastro.Core.Failures;

// Texto de cada código de falha, em Resources/Failures.resx (neutro, inglês), Failures.pt-BR.resx e Failures.es.resx.
// O português é o padrão e vai para o log e para a coluna de texto antiga; o MCP pede o idioma de quem usa. A tela
// traduz pelo código e pelos parâmetros, nos JSON de tradução.
public static class FailureText
{
    private static readonly ResourceManager Manager =
        new("Rastro.Core.Resources.Failures", typeof(FailureText).Assembly);

    // O idioma é sempre explícito. Sem ele, português: é o texto que vai para o log e para a coluna de texto antiga,
    // que não mudam com o idioma de quem usa (ADR-009).
    public static string Of(SurveyFailure code, IReadOnlyList<string>? args = null, UiLanguage language = UiLanguage.Portuguese) =>
        Format("Failure.Survey." + code, args, language);

    public static string Of(ValidationFailure code, IReadOnlyList<string>? args = null, UiLanguage language = UiLanguage.Portuguese) =>
        code == ValidationFailure.Analysis ? Nested(args ?? [], language) : Format("Failure.Validation." + code, args, language);

    public static string Of(ProposalRefusal code, IReadOnlyList<string>? args = null, UiLanguage language = UiLanguage.Portuguese) => code switch
    {
        ProposalRefusal.AnalysisFailed => Nested(args ?? [], language),
        ProposalRefusal.ProofTargetInvalid => Format("Failure.Refusal.ProofTargetInvalid", [Nested(args ?? [], language)], language),
        _ => Format("Failure.Refusal." + code, args, language)
    };

    public static string Of(DraftError code, IReadOnlyList<string>? args = null, UiLanguage language = UiLanguage.Portuguese) =>
        Format("Failure.Draft." + code, args, language);

    // Código aninhado: o primeiro parâmetro é o código do SurveyFailure e o resto são os parâmetros dele.
    public static IReadOnlyList<string> Nest(SurveyFailure code, IReadOnlyList<string>? args) =>
        [((int)code).ToString(CultureInfo.InvariantCulture), .. args ?? []];

    public static string Nested(IReadOnlyList<string> args, UiLanguage language = UiLanguage.Portuguese)
    {
        if (args.Count > 0 && int.TryParse(args[0], CultureInfo.InvariantCulture, out var code) && Enum.IsDefined((SurveyFailure)code))
            return Of((SurveyFailure)code, args.Skip(1).ToList(), language);
        return string.Empty;
    }

    public static string SerializeArgs(IReadOnlyList<string>? args) =>
        args is { Count: > 0 } ? JsonSerializer.Serialize(args) : "[]";

    public static IReadOnlyList<string> ParseArgs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            // Coluna gravada por outra versão ou editada à mão: sem parâmetros, a tela cai no texto antigo.
            return [];
        }
    }

    private static string Format(string key, IReadOnlyList<string>? args, UiLanguage language)
    {
        var culture = language.Culture();
        var template = Manager.GetString(key, culture) ?? key;
        return string.Format(culture, template, (args ?? []).Cast<object>().ToArray());
    }
}
