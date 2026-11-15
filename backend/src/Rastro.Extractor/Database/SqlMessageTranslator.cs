using System.Text.RegularExpressions;
using Rastro.Core.Localization;
using Txt = Rastro.Extractor.Resources.Database;

namespace Rastro.Extractor.Database;

// Os leitores de SQL (SqlParserCS para o PostgreSQL, ScriptDom para o T-SQL) dão a mensagem de erro em
// inglês, e ela vai para a tela. Aqui os formatos frequentes viram português ou espanhol, por padrão
// (regex), sem IA e sem depender de nada além do texto: a mesma mensagem dá sempre a mesma tradução. Em
// inglês, e no que não casa com nenhum padrão, fica o texto original do leitor.
public static class SqlMessageTranslator
{
    private static readonly Regex Expected = new(@"^Expected:?\s+(?<want>.+?),\s*found:?\s+(?<got>.+?)\.?$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex NearKeyword = new(@"^Incorrect syntax near the keyword '(?<x>.*)'\.?$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Near = new(@"^Incorrect syntax near '?(?<x>.*?)'?\.?$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Unclosed = new(@"^Unclosed quotation mark after the character string '(?<x>.*)'\.?$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Unterminated = new(@"^Unterminated (?<what>string literal|quoted identifier|dollar quoted string|quoted string|comment)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EndOfInput = new(@"^Unexpected (end of (input|file|statement)|EOF)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Parentheses = new(@"^(Unbalanced|Mismatched|Unmatched) parenthes[ei]s\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Translate(string message, UiLanguage language)
    {
        if (string.IsNullOrWhiteSpace(message) || language == UiLanguage.English) return message;
        var text = message.Trim();

        var m = Expected.Match(text);
        if (m.Success) return Txt.Get("Sql.Expected", language, Thing(m.Groups["want"].Value, language), Thing(m.Groups["got"].Value, language));

        m = Unclosed.Match(text);
        if (m.Success) return Txt.Get("Sql.Unclosed", language, m.Groups["x"].Value);

        m = NearKeyword.Match(text);
        if (m.Success) return Txt.Get("Sql.NearKeyword", language, m.Groups["x"].Value);

        m = Near.Match(text);
        if (m.Success) return Txt.Get("Sql.Near", language, m.Groups["x"].Value);

        m = Unterminated.Match(text);
        if (m.Success) return m.Groups["what"].Value.ToLowerInvariant() switch
        {
            "quoted identifier" => Txt.Get("Sql.UnterminatedIdentifier", language),
            "comment" => Txt.Get("Sql.UnterminatedComment", language),
            _ => Txt.Get("Sql.UnterminatedString", language)
        };

        if (EndOfInput.IsMatch(text)) return Txt.Get("Sql.EndOfInput", language);
        if (Parentheses.IsMatch(text)) return Txt.Get("Sql.Parentheses", language);
        return message;
    }

    // Um termo do "Expected X, found Y": os conhecidos viram o idioma pedido, o resto fica como veio.
    private static string Thing(string value, UiLanguage language)
    {
        var v = value.Trim();
        return v.ToLowerInvariant() switch
        {
            "an expression" or "expression" => Txt.Get("Sql.ThingExpression", language),
            "end of statement" => Txt.Get("Sql.ThingEndOfStatement", language),
            "eof" or "end of input" or "end of file" => Txt.Get("Sql.ThingEndOfText", language),
            "an identifier" or "identifier" => Txt.Get("Sql.ThingIdentifier", language),
            "a literal" or "literal" => Txt.Get("Sql.ThingLiteral", language),
            "a data type" or "data type" => Txt.Get("Sql.ThingDataType", language),
            "a keyword" or "keyword" => Txt.Get("Sql.ThingKeyword", language),
            "a table name" or "table name" => Txt.Get("Sql.ThingTableName", language),
            ")" => Txt.Get("Sql.ThingCloseParenthesis", language),
            "(" => Txt.Get("Sql.ThingOpenParenthesis", language),
            "," => Txt.Get("Sql.ThingComma", language),
            "a select statement" or "select" => Txt.Get("Sql.ThingSelect", language),
            _ => v
        };
    }
}
