using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Rastro.Core.Localization;

namespace Rastro.Core.Business;

// Regra de texto da decisão na visão de negócio (Fase 59): a condição em código vira uma frase legível,
// determinística e sem IA. Só humaniza o que o código mostra (identificadores em palavras, "this." fora, "!" na
// forma negativa, operadores em palavras): na dúvida, o texto humanizado literal. O mesmo código, o mesmo idioma
// (parâmetro explícito, nunca a cultura da thread) e a mesma saída. As palavras fixas vêm dos .resx do núcleo.
public static class BusinessLabelRule
{
    // Cabe o "?" (e o "¿" do espanhol) dentro do limite do texto do passo.
    public const int MaxBody = BusinessFlowBuilder.MaxLabelLength - 2;

    private const string Ellipsis = "…";
    private const string KeyPrefix = "business.rule.";

    private static readonly Regex WordBoundary = new(
        @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|[_\-]+", RegexOptions.CultureInvariant);

    private static readonly Regex IsNotNull = new(
        @"\s+is\s+not\s+null\b|\s+is\s*\{\s*\}(\s+[A-Za-z_]\w*)?", RegexOptions.CultureInvariant);

    private static readonly Regex IsNull = new(
        @"\s+is\s+null\b|\s+is\s+not\s*\{\s*\}", RegexOptions.CultureInvariant);

    // Nome que começa com verbo ("IsDefined"): a negação entra depois do verbo ("is not defined").
    private static readonly HashSet<string> LeadingVerbs = new(StringComparer.Ordinal) { "is", "has", "can", "should", "was", "are", "does" };

    private enum TokenKind { Word, Text, Null, Op, Open, Close }

    private sealed record Token(TokenKind Kind, string Text, IReadOnlyList<string>? Segments = null);

    // A condição sem o "if (...)" em volta. Devolve o texto humanizado, sem o "?" final, com até MaxBody caracteres.
    public static string Humanize(string? condition, UiLanguage language)
    {
        var raw = (condition ?? string.Empty).Trim().TrimEnd('?', ' ');
        if (raw.Length == 0) return string.Empty;
        // Padrões do C# que significam comparação com nulo: "x is { } v" é x != null, "x is not null" é x != null.
        raw = IsNotNull.Replace(raw, " != null");
        raw = IsNull.Replace(raw, " == null");

        var tokens = Tokenize(raw);
        var full = Finish(Render(tokens, language));
        if (full.Length == 0) return Cut(raw);
        if (full.Length <= MaxBody) return full;

        // Condição composta longa: fica a primeira parte, com reticências.
        var first = FirstPart(tokens);
        if (first.Count > 0 && first.Count < tokens.Count)
        {
            var head = Finish(Render(first, language));
            if (head.Length > 0 && head.Length < MaxBody) return head + Ellipsis;
        }
        return Cut(full);
    }

    private static string Cut(string text) =>
        text.Length <= MaxBody ? text : text[..(MaxBody - Ellipsis.Length)].TrimEnd() + Ellipsis;

    private static string Finish(string text)
    {
        var clean = Regex.Replace(text, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        if (clean.Length == 0 || !char.IsLetter(clean[0])) return clean;
        return char.ToUpper(clean[0], CultureInfo.InvariantCulture) + clean[1..];
    }

    // ---------- leitura da condição ----------

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Texto literal (com $ ou @ na frente, no C#).
            var quoteAt = i;
            while (quoteAt < text.Length && (text[quoteAt] == '$' || text[quoteAt] == '@')) quoteAt++;
            if (quoteAt < text.Length && (text[quoteAt] == '"' || text[quoteAt] == '\''))
            {
                var end = EndOfLiteral(text, quoteAt);
                tokens.Add(new Token(TokenKind.Text, text[quoteAt..end]));
                i = end;
                continue;
            }

            if (char.IsDigit(c))
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '.' || text[i] == '_')) i++;
                tokens.Add(new Token(TokenKind.Text, text[start..i]));
                continue;
            }

            if (char.IsLetter(c) || c == '_' || c == '@')
            {
                i = ReadPath(text, i, tokens);
                continue;
            }

            var op = OperatorAt(text, i);
            if (op is not null)
            {
                tokens.Add(op == "(" ? new Token(TokenKind.Open, op) : op == ")" ? new Token(TokenKind.Close, op) : new Token(TokenKind.Op, op));
                i += op.Length;
                continue;
            }

            tokens.Add(new Token(TokenKind.Text, c.ToString()));
            i++;
        }
        return tokens;
    }

    private static readonly string[] Operators = ["===", "!==", "==", "!=", ">=", "<=", "&&", "||", ">", "<", "!", "(", ")"];

    private static string? OperatorAt(string text, int index) =>
        Operators.FirstOrDefault(o => string.CompareOrdinal(text, index, o, 0, o.Length) == 0);

    private static int EndOfLiteral(string text, int quoteAt)
    {
        var quote = text[quoteAt];
        var i = quoteAt + 1;
        while (i < text.Length)
        {
            if (text[i] == '\\') i += 2;
            else if (text[i] == quote) return i + 1;
            else i++;
        }
        return text.Length;
    }

    // Caminho (a.b?.c) e, se vier chamada logo depois, descarta os argumentos: DentroDoPrazo(id) vira DentroDoPrazo.
    private static int ReadPath(string text, int start, List<Token> tokens)
    {
        var segments = new List<string>();
        var i = start;
        while (true)
        {
            var from = i;
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '@' || text[i] == '$')) i++;
            segments.Add(text[from..i].TrimStart('@'));

            if (i + 1 < text.Length && text[i] == '.' && StartsIdentifier(text[i + 1])) i++;
            else if (i + 2 < text.Length && text[i] == '?' && text[i + 1] == '.' && StartsIdentifier(text[i + 2])) i += 2;
            else if (i + 2 < text.Length && text[i] == '!' && text[i + 1] == '.' && StartsIdentifier(text[i + 2])) i += 2;
            else break;
        }

        if (i < text.Length && text[i] == '(') i = EndOfGroup(text, i);

        if (segments.Count > 1 && segments[0] == "this") segments.RemoveAt(0);

        if (segments.Count == 1 && segments[0] is "null" or "undefined")
            tokens.Add(new Token(TokenKind.Null, segments[0]));
        else
            tokens.Add(new Token(TokenKind.Word, string.Join('.', segments), segments));
        return i;
    }

    private static bool StartsIdentifier(char c) => char.IsLetter(c) || c == '_' || c == '@';

    private static int EndOfGroup(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return i + 1;
        }
        return text.Length;
    }

    // ---------- escrita ----------

    private static string Render(IReadOnlyList<Token> tokens, UiLanguage language)
    {
        var output = new List<string>();
        var i = 0;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            switch (token.Kind)
            {
                case TokenKind.Word:
                    output.Add(Words(token.Segments!));
                    break;
                case TokenKind.Text:
                    output.Add(token.Text);
                    break;
                case TokenKind.Null:
                    output.Add(Word("empty", language));
                    break;
                case TokenKind.Open:
                {
                    var close = Matching(tokens, i);
                    var inner = tokens.Skip(i + 1).Take(close - i - 1).ToList();
                    var text = Render(inner, language);
                    output.Add(HasLogic(inner) ? $"({text})" : text);
                    i = close;
                    break;
                }
                case TokenKind.Op when token.Text == "!":
                    i = Negate(tokens, i, language, output);
                    break;
                case TokenKind.Op:
                    output.Add(OperatorWord(token.Text, language));
                    break;
            }
            i++;
        }
        return string.Join(' ', output.Where(o => o.Length > 0));
    }

    // "!" sobre uma palavra ou um grupo. Devolve o índice do último token consumido.
    private static int Negate(IReadOnlyList<Token> tokens, int at, UiLanguage language, List<string> output)
    {
        var not = Word("not", language);
        if (at + 1 >= tokens.Count)
        {
            output.Add(not);
            return at;
        }

        var next = tokens[at + 1];
        if (next.Kind == TokenKind.Word)
        {
            output.Add(NegatedWord(next.Segments!, not, language));
            return at + 1;
        }

        if (next.Kind == TokenKind.Open)
        {
            var close = Matching(tokens, at + 1);
            var inner = tokens.Skip(at + 2).Take(close - at - 2).ToList();
            if (HasLogic(inner))
            {
                output.Add($"{not} ({Render(inner, language)})");
            }
            else if (inner.Count == 1 && inner[0].Kind == TokenKind.Word)
            {
                output.Add(NegatedWord(inner[0].Segments!, not, language));
            }
            else if (Flip(inner) is { } flipped)
            {
                output.Add(Render(flipped, language));
            }
            else
            {
                output.Add($"{not} {Render(inner, language)}");
            }
            return close;
        }

        output.Add(not);
        return at;
    }

    // Uma só comparação no nível de cima: !(a == b) vira a != b, sem inventar sentido.
    private static List<Token>? Flip(List<Token> inner)
    {
        var depth = 0;
        var found = -1;
        for (var i = 0; i < inner.Count; i++)
        {
            switch (inner[i].Kind)
            {
                case TokenKind.Open: depth++; break;
                case TokenKind.Close: depth--; break;
                case TokenKind.Op when depth == 0 && IsComparison(inner[i].Text):
                    if (found >= 0) return null;
                    found = i;
                    break;
            }
        }
        if (found < 0) return null;

        var opposite = inner[found].Text switch
        {
            "==" or "===" => "!=",
            "!=" or "!==" => "==",
            ">" => "<=",
            "<" => ">=",
            ">=" => "<",
            "<=" => ">",
            _ => null
        };
        if (opposite is null) return null;
        var copy = inner.ToList();
        copy[found] = new Token(TokenKind.Op, opposite);
        return copy;
    }

    private static bool IsComparison(string op) => op is "==" or "===" or "!=" or "!==" or ">" or "<" or ">=" or "<=";

    private static bool HasLogic(IEnumerable<Token> tokens) =>
        tokens.Any(t => t.Kind == TokenKind.Op && t.Text is "&&" or "||");

    private static int Matching(IReadOnlyList<Token> tokens, int open)
    {
        var depth = 0;
        for (var i = open; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == TokenKind.Open) depth++;
            else if (tokens[i].Kind == TokenKind.Close && --depth == 0) return i;
        }
        return tokens.Count;
    }

    // Antes do primeiro && ou || do nível de cima.
    private static List<Token> FirstPart(IReadOnlyList<Token> tokens)
    {
        var depth = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.Open: depth++; break;
                case TokenKind.Close: depth--; break;
                case TokenKind.Op when depth == 0 && tokens[i].Text is "&&" or "||":
                    return tokens.Take(i).ToList();
            }
        }
        return tokens.ToList();
    }

    private static string OperatorWord(string op, UiLanguage language) => op switch
    {
        "&&" => Word("and", language),
        "||" => Word("or", language),
        "==" or "===" => Word("is", language),
        "!=" or "!==" => Word("isNot", language),
        ">" => Word("gt", language),
        ">=" => Word("gte", language),
        "<" => Word("lt", language),
        "<=" => Word("lte", language),
        _ => op
    };

    private static string Word(string key, UiLanguage language) => LocalizedText.Get(KeyPrefix + key, language);

    // Com receptor (pedido.enviado), a negação vai antes do último nome: "pedido não enviado".
    private static string NegatedWord(IReadOnlyList<string> segments, string not, UiLanguage language)
    {
        var last = Humanized(segments[^1]).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // "IsDefined": em inglês, a negação vem depois do verbo ("is not defined"). Nos outros idiomas o verbo do nome
        // continua em inglês, e o "não" no meio dele ficava ilegível ("is não defined"): a negação vem antes da
        // expressão inteira, entre parênteses, como na condição composta ("não (ui languages is defined)").
        if (last.Length > 1 && LeadingVerbs.Contains(last[0]))
        {
            var phrase = segments.Count < 2 ? string.Join(' ', last) : $"{Words(segments.Take(segments.Count - 1).ToList())} {string.Join(' ', last)}";
            return language == UiLanguage.English ? NegatedAfterVerb(segments, last, not) : $"{not} ({phrase})";
        }
        var member = $"{not} {string.Join(' ', last)}";
        return segments.Count < 2
            ? member
            : $"{Words(segments.Take(segments.Count - 1).ToList())} {member}";
    }

    private static string NegatedAfterVerb(IReadOnlyList<string> segments, string[] last, string not)
    {
        var member = $"{last[0]} {not} {string.Join(' ', last.Skip(1))}";
        return segments.Count < 2 ? member : $"{Words(segments.Take(segments.Count - 1).ToList())} {member}";
    }

    private static string Words(IReadOnlyList<string> segments) =>
        string.Join(' ', segments.Select(Humanized).Where(s => s.Length > 0));

    // PascalCase, camelCase e snake_case em palavras minúsculas; sigla (CPF) fica como está.
    private static string Humanized(string identifier)
    {
        var parts = WordBoundary.Split(identifier).Where(p => p.Length > 0);
        return string.Join(' ', parts.Select(p => p.Length > 1 && p.All(char.IsUpper) ? p : p.ToLowerInvariant()));
    }
}
