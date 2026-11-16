using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Rastro.Extractor.Database;

// O ScriptDom traduz a mensagem do erro de leitura pelo idioma do Windows ("Sintaxe incorreta próxima
// a" numa máquina em português). O Rastro traduz pelo idioma do pedido (SqlMessageTranslator), a
// partir do inglês: o leitor roda sempre com a cultura invariante, que cai no texto em inglês.
internal static class TSqlParsing
{
    public static TSqlFragment? Parse(string text, out IList<ParseError> errors)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            using var reader = new StringReader(text);
            return parser.Parse(reader, out errors);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
