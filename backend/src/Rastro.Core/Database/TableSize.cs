using System.Globalization;
using Rastro.Core.Localization;

namespace Rastro.Core.Database;

// O tamanho estimado de uma tabela (Fase 44), vindo do catálogo do banco vivo (DbTable.EstimatedRows). Sem
// banco vivo não há estimativa, e as regras que dependem do tamanho se comportam como antes.
public static class TableSize
{
    // Abaixo disto a tabela é pequena: ler tudo dela não é achado (a leitura sem limite).
    public const long SmallRows = 1_000;

    // Daqui para cima a tabela é grande: a exceção da lista filha deixa de valer, porque o pai (o espaço, o
    // projeto) também pode ter listas enormes.
    public const long LargeRows = 100_000;

    public static bool IsSmall(DbTable table) => table.EstimatedRows is { } rows && rows < SmallRows;

    public static bool IsLarge(DbTable table) => table.EstimatedRows is { } rows && rows >= LargeRows;

    // "250.000" em português, "250,000" em inglês e "250.000" em espanhol: o separador de milhar sai da cultura
    // do idioma pedido (explícita), nunca da cultura da máquina.
    public static string Format(long rows, UiLanguage language) => rows.ToString("N0", language.Culture());
}
