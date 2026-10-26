using Rastro.Core.Graph;

namespace Rastro.Core.Validation;

// Passo novo do rascunho contra um passo levantado. "Tipo.Metodo" casa pelo nome; com os tipos
// ("Tipo.Metodo(string, int)") casa também pela assinatura do símbolo, para a sobrecarga certa.
// Tipos comparados pelo último segmento e sem distinguir alias (string = String, int = Int32).
public static class StepSignature
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["string"] = "string",
        ["int32"] = "int",
        ["int"] = "int",
        ["int64"] = "long",
        ["long"] = "long",
        ["int16"] = "short",
        ["short"] = "short",
        ["boolean"] = "bool",
        ["bool"] = "bool",
        ["decimal"] = "decimal",
        ["double"] = "double",
        ["single"] = "float",
        ["float"] = "float",
        ["object"] = "object",
        ["byte"] = "byte",
        ["char"] = "char",
    };

    public static bool Matches(string draftLabel, FlowNode node)
    {
        var open = draftLabel.IndexOf('(');
        if (open < 0) return node.Label == draftLabel.Trim();

        var name = draftLabel[..open].Trim();
        if (node.Label != name || node.Symbol is null) return false;
        var symbolOpen = node.Symbol.IndexOf('(');
        if (symbolOpen < 0) return false;
        return Parameters(draftLabel[open..]).SequenceEqual(Parameters(node.Symbol[symbolOpen..]));
    }

    // "(System.String, int, List<Ns.Alert>)" vira ["string", "int", "list<alert>"].
    private static List<string> Parameters(string list)
    {
        var inner = list.Trim();
        if (inner.StartsWith('(')) inner = inner[1..];
        if (inner.EndsWith(')')) inner = inner[..^1];
        if (string.IsNullOrWhiteSpace(inner)) return [];
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] is '<' or '[') depth++;
            else if (inner[i] is '>' or ']') depth--;
            else if (inner[i] == ',' && depth == 0)
            {
                parts.Add(Normalize(inner[start..i]));
                start = i + 1;
            }
        }
        parts.Add(Normalize(inner[start..]));
        return parts;
    }

    private static string Normalize(string type)
    {
        var clean = type.Trim();
        // Nome do parâmetro escrito pelo usuário ("string mensagem") fica de fora.
        var generic = clean.IndexOf('<');
        var space = clean.LastIndexOf(' ');
        if (space > 0 && (generic < 0 || space > clean.LastIndexOf('>'))) clean = clean[..space].Trim();
        generic = clean.IndexOf('<');
        var head = generic < 0 ? clean : clean[..generic];
        var tail = generic < 0 ? "" : clean[(generic + 1)..clean.LastIndexOf('>')];
        var shortName = head.Split('.').Last();
        shortName = Aliases.GetValueOrDefault(shortName, shortName);
        var args = tail.Length == 0 ? "" : "<" + string.Join(",", Parameters(tail)) + ">";
        return (shortName + args).ToLowerInvariant();
    }
}
