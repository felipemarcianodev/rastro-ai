using Rastro.Core.Database;

namespace Rastro.Extractor.Database;

// Um leitor de SQL por dialeto (ADR-006): o SQL Server pelo ScriptDom (Fase 37) e o PostgreSQL por um
// leitor a mais (Fase 42, PgSqlSchemaReader), sem mudar o modelo nem a fusão. Lê o texto, nunca executa nada.
public interface ISqlSchemaReader
{
    SqlDialect Dialect { get; }

    // file: caminho relativo ao repositório; firstLine: a linha do arquivo onde o texto começa (o SQL
    // dentro de uma migration começa no meio do .cs). kind: a fonte que os itens lidos recebem.
    SchemaFragment Read(string text, string file, int firstLine, SchemaSourceKind kind);
}

public static class SqlSchemaReaders
{
    private static readonly ISqlSchemaReader[] All = [new TSqlSchemaReader(), new PgSqlSchemaReader()];

    public static ISqlSchemaReader? For(SqlDialect dialect) => All.FirstOrDefault(r => r.Dialect == dialect);
}
