using System.Text;
using System.Text.RegularExpressions;

namespace Rastro.Core.Http;

// Comparação de rotas HTTP, pura e sem estado. O template vem do atendente ("api/pedidos/{id:int}");
// o chamado vem de quem chama ("api/pedidos/{}/cancelamento", onde {} é um buraco que só existe em
// tempo de execução).
public static partial class RouteTemplate
{
    // Marca de um pedaço não constante da URL chamada.
    public const string Hole = "{}";

    // Tira esquema e host, query, fragmento e barras das pontas; minúsculas fora das chaves.
    public static string Normalize(string route)
    {
        var text = route.Trim();
        text = SchemeAndHost().Replace(text, string.Empty);

        var cut = text.IndexOfAny(['?', '#']);
        if (cut >= 0) text = text[..cut];

        if (text.StartsWith("~/", StringComparison.Ordinal)) text = text[2..];
        text = text.Trim('/');

        var result = new StringBuilder(text.Length);
        var depth = 0;
        foreach (var c in text)
        {
            if (c == '{') depth++;
            result.Append(depth > 0 ? c : char.ToLowerInvariant(c));
            if (c == '}' && depth > 0) depth--;
        }
        return result.ToString();
    }

    // Casamento exato por segmento. No template, {x}, {x:int} e {*x} casam com qualquer segmento
    // (o catch-all com o resto da rota). No chamado, o buraco {} casa com qualquer segmento do template.
    public static bool Matches(string template, string called)
    {
        var t = Segments(template);
        var c = Segments(called);

        for (var i = 0; i < t.Length; i++)
        {
            if (IsCatchAll(t[i])) return true;
            if (i >= c.Length || !SegmentMatches(t[i], c[i])) return false;
        }
        return t.Length == c.Length;
    }

    // O chamado casa com os ÚLTIMOS segmentos do template: serve quando o começo da URL é um endereço
    // base que veio de configuração e não deu para ler. Exige 2 segmentos ou mais no chamado, senão
    // "estornos" sozinho casaria com qualquer rota que termine assim.
    public static bool MatchesSuffix(string template, string called)
    {
        var t = Segments(template);
        var c = Segments(called);
        if (c.Length < 2 || c.Length > t.Length) return false;

        var offset = t.Length - c.Length;
        for (var i = 0; i < c.Length; i++)
            if (!SegmentMatches(t[offset + i], c[i])) return false;
        return true;
    }

    private static string[] Segments(string route) =>
        Normalize(route).Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsCatchAll(string segment) => segment.StartsWith("{*", StringComparison.Ordinal);

    private static bool SegmentMatches(string template, string called)
    {
        // Buraco da chamada: qualquer coisa pode estar ali.
        if (called.Contains(Hole, StringComparison.Ordinal)) return true;
        if (!template.Contains('{')) return string.Equals(template, called, StringComparison.OrdinalIgnoreCase);

        // Segmento com parâmetro (inteiro ou misturado com texto, como "v{versao}"): vira regex.
        var pattern = new StringBuilder("^");
        var rest = template;
        while (rest.Length > 0)
        {
            var open = rest.IndexOf('{');
            if (open < 0)
            {
                pattern.Append(Regex.Escape(rest));
                break;
            }
            pattern.Append(Regex.Escape(rest[..open]));
            var close = rest.IndexOf('}', open);
            if (close < 0)
            {
                pattern.Append(Regex.Escape(rest[open..]));
                break;
            }
            pattern.Append(".+");
            rest = rest[(close + 1)..];
        }
        pattern.Append('$');
        return Regex.IsMatch(called, pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*://[^/?#]*")]
    private static partial Regex SchemeAndHost();
}
