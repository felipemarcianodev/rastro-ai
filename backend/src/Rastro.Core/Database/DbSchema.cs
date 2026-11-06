namespace Rastro.Core.Database;

// Esquema do banco (Fase 37, ADR-006): tabelas, colunas, chaves, índices, views e procedures, lidos do
// código do repositório (modelo do EF Core, migrations e .sql). Determinístico e sem IA (ADR-001): o
// mesmo código dá sempre o mesmo esquema. Nada aqui é amarrado ao SQL Server: o dialeto é um campo,
// e o tipo da coluna fica como o banco escreve ("nvarchar(30)", "jsonb"), sem tradução genérica.

public enum SqlDialect
{
    SqlServer = 0,
    PostgreSql = 1
}

// De onde veio cada item. A ordem é a de prioridade na fusão (o primeiro dá o nome e o padrão).
public enum SchemaSourceKind
{
    // DbContext, DbSet, atributos, OnModelCreating e IEntityTypeConfiguration.
    EfModel = 0,

    // As migrations do EF Core, aplicadas em ordem (o estado final).
    EfMigration = 1,

    // Arquivos .sql e projeto de banco.
    SqlScript = 2,

    // Catálogo de um banco vivo (Fase 40).
    LiveDatabase = 3
}

public enum DeleteAction
{
    NoAction = 0,
    Cascade = 1,
    SetNull = 2,
    SetDefault = 3,
    Restrict = 4
}

public enum SchemaDivergenceKind
{
    // As fontes dão tipos diferentes para a mesma coluna.
    ColumnType = 0,

    // Uma fonte aceita nulo e a outra não.
    ColumnNullability = 1,

    // A coluna existe no modelo e não na migration (ou o contrário).
    ColumnMissing = 2,

    // As fontes dão chaves primárias diferentes para a mesma tabela.
    PrimaryKey = 3
}

// Arquivo relativo ao repositório e linha (1 em diante). No banco vivo, File é "banco vivo" e Line é 0.
public record SchemaSource(SchemaSourceKind Kind, string File, int Line);

// Type e Nullable ficam nulos quando as fontes divergem: o Rastro não escolhe (ver Divergences).
// Personal (Fase 38): o nome da coluna é de dado pessoal (CPF, e-mail, telefone), pelo nome e nunca
// pelo conteúdo, que o Rastro não lê (ver PersonalData).
public record DbColumn(string Name, string? Type, bool? Nullable, string? Default, bool Identity, IReadOnlyList<SchemaSource> Sources, bool Personal = false);

public record DbPrimaryKey(string? Name, IReadOnlyList<string> Columns, SchemaSource Source);

// PrincipalTable no formato de DbNames.Display (sem o schema padrão do dialeto).
public record DbForeignKey(string? Name, IReadOnlyList<string> Columns, string PrincipalTable, IReadOnlyList<string> PrincipalColumns, DeleteAction OnDelete, SchemaSource Source);

// Convention (Fase 38): índice que o EF Core cria sozinho em cada chave estrangeira, deduzido do modelo
// (não está escrito no código). Só aparece quando a tabela não tem migration nem .sql, que dizem o que
// o banco de fato tem.
// Method (Fase 43a): o método de acesso do PostgreSQL, em minúsculas ("btree", "gin", "gist", "hash"), quando a
// fonte o escreve; nulo é o padrão (btree). OperatorClasses: a classe de operador de cada coluna, na ordem de
// Columns, vazia onde não há ("gin_trgm_ops" marca o índice de trigrama, que atende LIKE e ILIKE com curinga).
public record DbIndex(string? Name, IReadOnlyList<string> Columns, IReadOnlyList<string> Included, bool Unique, SchemaSource Source, bool Convention = false,
    string? Method = null, IReadOnlyList<string>? OperatorClasses = null)
{
    // Índice de busca por trecho de texto: GIN ou GiST com a classe de trigrama (pg_trgm) na coluna.
    public bool IsTrigram(string column)
    {
        if (Method is not ("gin" or "gist") || OperatorClasses is null) return false;
        var at = Columns.ToList().FindIndex(c => DbNames.Unquote(c).Equals(DbNames.Unquote(column), StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at < OperatorClasses.Count && OperatorClasses[at].Contains("trgm", StringComparison.OrdinalIgnoreCase);
    }
}

// Schema nulo é o padrão do dialeto (dbo no SQL Server, public no PostgreSQL). Entity (Fase 39): a
// classe da entidade no modelo do EF, que liga o passo do fluxo (DbSet<Entity>) à tabela.
public record DbTable(
    string? Schema,
    string Name,
    IReadOnlyList<DbColumn> Columns,
    DbPrimaryKey? PrimaryKey,
    IReadOnlyList<DbForeignKey> ForeignKeys,
    IReadOnlyList<DbIndex> Indexes,
    IReadOnlyList<SchemaSource> Sources,
    string? Entity = null);

// Columns: o que a view devolve, quando o nome sai do texto (coluna sem alias numa expressão fica de fora).
public record DbView(string? Schema, string Name, IReadOnlyList<string> Columns, IReadOnlyList<string> Reads, SchemaSource Source);

public record DbParameter(string Name, string Type, bool Output);

public record DbProcedure(string? Schema, string Name, IReadOnlyList<DbParameter> Parameters, IReadOnlyList<string> Reads, IReadOnlyList<string> Writes, SchemaSource Source);

public record DivergentValue(string Value, SchemaSource Source);

// Column vazio na divergência de chave primária.
public record SchemaDivergence(SchemaDivergenceKind Kind, string Table, string Column, IReadOnlyList<DivergentValue> Values);

// O que o leitor viu e não conseguiu ler (arquivo que o leitor de SQL não entendeu, configuração fora do alcance).
public record SchemaNote(string Message, string File, int Line);

// Achado do esquema, sem IA (Fase 38). A mesma entrada dá sempre os mesmos achados.
public enum SchemaFindingKind
{
    // A migration ou o .sql cria a chave estrangeira sem um índice que comece pelas colunas dela:
    // toda busca pela FK (e todo DELETE na tabela de destino) varre a tabela.
    ForeignKeyWithoutIndex = 0
}

// Source: onde a chave estrangeira foi criada (a migration ou o .sql).
public record SchemaFinding(SchemaFindingKind Kind, string Table, IReadOnlyList<string> Columns, SchemaSource Source);

public record DbSchema(
    SqlDialect Dialect,
    IReadOnlyList<DbTable> Tables,
    IReadOnlyList<DbView> Views,
    IReadOnlyList<DbProcedure> Procedures,
    IReadOnlyList<SchemaDivergence> Divergences,
    IReadOnlyList<SchemaNote> Notes,
    IReadOnlyList<SchemaFinding> Findings);

// O que uma fonte leu, antes da fusão: cada coluna, chave e índice com uma fonte só.
public record SchemaFragment(
    IReadOnlyList<DbTable> Tables,
    IReadOnlyList<DbView> Views,
    IReadOnlyList<DbProcedure> Procedures,
    IReadOnlyList<SchemaNote> Notes)
{
    public static SchemaFragment Empty { get; } = new([], [], [], []);
}
