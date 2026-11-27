using System.Text.RegularExpressions;
using Rastro.Core.Business;
using Rastro.Core.Localization;

namespace Rastro.Core.Graph;

// Texto de leitura do rótulo de um ramo (D-02 e D-10): "catch (Ex)" vira "Se der erro (Ex)", "finally" vira "Sempre
// ao final" e "case Outcome.FolderNotFound" vira "Caso folder not found". Só mexe nesses três; "then", "else",
// "default" e qualquer outro rótulo voltam como vieram. O idioma é parâmetro explícito. O rótulo bruto continua
// no grafo (e no código), este texto é só para quem lê.
public static partial class BranchText
{
    private const string FinallyLabel = "finally";
    private const string CatchLabel = "catch";
    private const string CasePrefix = "case ";
    private const string CaseSeparator = " | ";

    public static string Plain(string? label, UiLanguage language)
    {
        if (string.IsNullOrEmpty(label)) return label ?? string.Empty;
        if (string.Equals(label, FinallyLabel, StringComparison.Ordinal))
            return LocalizedText.Get("Branch.Finally", language);

        if (string.Equals(label, CatchLabel, StringComparison.Ordinal) || label.StartsWith(CatchLabel + " ", StringComparison.Ordinal)
            || label.StartsWith(CatchLabel + "(", StringComparison.Ordinal))
        {
            var type = CatchType().Match(label) is { Success: true } match ? match.Groups[1].Value.Trim() : string.Empty;
            return type.Length == 0
                ? LocalizedText.Get("Branch.Catch", language)
                : LocalizedText.Get("Branch.CatchType", language, type);
        }

        if (!label.StartsWith(CasePrefix, StringComparison.Ordinal)) return label;
        var parts = label.Split(CaseSeparator).Select(part => part.StartsWith(CasePrefix, StringComparison.Ordinal)
            ? BusinessLabelRule.CaseValueText(part[CasePrefix.Length..])
            : part).ToList();
        var text = LocalizedText.Get("Branch.Case", language, parts[0]);
        for (var i = 1; i < parts.Count; i++) text = LocalizedText.Get("Branch.Or", language, text, parts[i]);
        return text;
    }

    [GeneratedRegex(@"^catch\s*\((.*)\)$")]
    private static partial Regex CatchType();
}
