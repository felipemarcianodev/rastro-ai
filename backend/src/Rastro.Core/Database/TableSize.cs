using System.Globalization;

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

    // "250.000" (separador de milhar do português, sem depender da cultura da máquina).
    public static string Format(long rows) => rows.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
}
