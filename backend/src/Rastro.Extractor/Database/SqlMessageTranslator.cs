using System.Text.RegularExpressions;

namespace Rastro.Extractor.Database;

// Os leitores de SQL (SqlParserCS para o PostgreSQL, ScriptDom para o T-SQL) dão a mensagem de erro em
// inglês, e ela vai para a tela. Aqui os formatos frequentes viram português, por padrão (regex), sem IA
// e sem depender de nada além do texto: a mesma mensagem dá sempre a mesma tradução. O que não casa com
// nenhum padrão fica com o texto original.
public static class SqlMessageTranslator
{
    private static readonly Regex Expected = new(@"^Expected:?\s+(?<want>.+?),\s*found:?\s+(?<got>.+?)\.?$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex NearKeyword = new(@"^Incorrect syntax near the keyword '(?<x>.*)'\.?$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Near = new(@"^Incorrect syntax near '?(?<x>.*?)'?\.?$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Unclosed = new(@"^Unclosed quotation mark after the character string '(?<x>.*)'\.?$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Unterminated = new(@"^Unterminated (?<what>string literal|quoted identifier|dollar quoted string|quoted string|comment)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EndOfInput = new(@"^Unexpected (end of (input|file|statement)|EOF)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Parentheses = new(@"^(Unbalanced|Mismatched|Unmatched) parenthes[ei]s\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Translate(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return message;
        var text = message.Trim();

        var m = Expected.Match(text);
        if (m.Success) return $"esperava {Thing(m.Groups["want"].Value)}, mas encontrou {Thing(m.Groups["got"].Value)}";

        m = Unclosed.Match(text);
        if (m.Success) return $"aspas sem fechar depois do texto '{m.Groups["x"].Value}'";

        m = NearKeyword.Match(text);
        if (m.Success) return $"sintaxe incorreta perto da palavra-chave '{m.Groups["x"].Value}'";

        m = Near.Match(text);
        if (m.Success) return $"sintaxe incorreta perto de '{m.Groups["x"].Value}'";

        m = Unterminated.Match(text);
        if (m.Success) return m.Groups["what"].Value.ToLowerInvariant() switch
        {
            "quoted identifier" => "identificador entre aspas sem fechar",
            "comment" => "comentário sem fechar",
            _ => "texto entre aspas sem fechar"
        };

        if (EndOfInput.IsMatch(text)) return "o texto terminou antes da hora";
        if (Parentheses.IsMatch(text)) return "parênteses sem par";
        return message;
    }

    // Um termo do "Expected X, found Y": os conhecidos viram português, o resto fica como veio.
    private static string Thing(string value)
    {
        var v = value.Trim();
        return v.ToLowerInvariant() switch
        {
            "an expression" or "expression" => "uma expressão",
            "end of statement" => "o fim da instrução",
            "eof" or "end of input" or "end of file" => "o fim do texto",
            "an identifier" or "identifier" => "um identificador",
            "a literal" or "literal" => "um valor literal",
            "a data type" or "data type" => "um tipo de dado",
            "a keyword" or "keyword" => "uma palavra-chave",
            "a table name" or "table name" => "um nome de tabela",
            ")" => "o parêntese de fechamento ')'",
            "(" => "o parêntese de abertura '('",
            "," => "uma vírgula",
            "a select statement" or "select" => "um SELECT",
            _ => v
        };
    }
}
