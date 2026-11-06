namespace Rastro.Core.Database;

// Análise de uma consulta SQL (Fase 40, ADR-006): achados de gargalo pelo leitor de SQL do dialeto,
// cruzados com o esquema lido do código. Determinística e sem IA (ADR-001): a mesma consulta e o mesmo
// esquema dão sempre os mesmos achados. Nada aqui executa a consulta nem lê dados.

public enum QueryFindingKind
{
    // SELECT * (ou t.*): traz colunas que ninguém pediu, inclusive as de dado pessoal.
    SelectStar = 0,

    // Função, conversão ou conta sobre a coluna do filtro: roda em cada linha e impede o índice.
    FunctionOnFilterColumn = 1,

    // LIKE com curinga no começo ('%...'): o índice da coluna não ajuda.
    LeadingWildcardLike = 2,

    // OR entre colunas diferentes: um índice só não atende o filtro inteiro.
    OrAcrossColumns = 3,

    // Filtro ou junção numa coluna sem índice que comece por ela (pelo esquema).
    ColumnWithoutIndex = 4,

    // Junção sem condição (CROSS JOIN, vírgula no FROM sem ligação no WHERE, ON sem coluna das duas tabelas).
    JoinWithoutCondition = 5,

    // DISTINCT numa consulta com junção: costuma esconder linhas repetidas pela junção.
    DistinctOverJoin = 6,

    // Subconsulta que cita a consulta de fora: roda uma vez por linha.
    CorrelatedSubquery = 7,

    // ORDER BY sem TOP nem OFFSET/FETCH: ordena todas as linhas.
    OrderByWithoutLimit = 8,

    // NOT IN de uma subconsulta cuja coluna aceita nulo: um nulo faz a consulta não devolver nada.
    NotInNullable = 9,

    // UPDATE ou DELETE sem WHERE: muda a tabela inteira.
    WriteWithoutWhere = 10,

    // SELECT sem LIMIT, TOP nem FETCH numa tabela filtrada só por igualdade em coluna que não identifica a
    // linha (nem chave primária, nem única): o caso típico é só pelo tenant. O resultado cresce com a tabela.
    ReadWithoutLimit = 11
}

// Mesma escala do FindingSeverity: o valor maior é o mais grave. A lista na tela ordena do maior para o
// menor (alta primeiro).
public enum QuerySeverity
{
    Low = 0,
    Medium = 1,
    High = 2
}

// Start e Length: o trecho no texto enviado, em caracteres (Start a partir de 0). Line: a linha do
// trecho (1 em diante). Snippet: o trecho com os espaços juntados, curto. Table: a tabela de que o
// achado fala, como o esquema escreve (DbNames.Display), quando há.
public record QueryFinding(QueryFindingKind Kind, QuerySeverity Severity, int Start, int Length, int Line, string Snippet, string Message, string? Table = null);

// Uma tabela citada pela consulta. InSchema falso: ela não está no esquema lido do código, e os achados
// que dependem dele (índice, nulo) não valem para ela.
public record QueryTableUse(string Name, string? Alias, bool InSchema);

public record QueryParseError(string Message, int Line, int Column);

// Error: o leitor de SQL não entendeu o texto; os achados vêm vazios.
public record QueryAnalysis(
    SqlDialect Dialect,
    int Statements,
    IReadOnlyList<QueryFinding> Findings,
    IReadOnlyList<QueryTableUse> Tables,
    QueryParseError? Error);

public static class QueryLimits
{
    // Consulta maior que isso não é uma consulta para analisar à mão.
    public const int MaxSqlLength = 100_000;
}
