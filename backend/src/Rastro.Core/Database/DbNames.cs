namespace Rastro.Core.Database;

// Nomes de tabela e de tipo comparáveis entre fontes: o modelo escreve "pedidos", a migration
// "dbo.pedidos" e o .sql "[dbo].[Pedidos]", e os três são a mesma tabela.
public static class DbNames
{
    public static string DefaultSchema(SqlDialect dialect) => dialect switch
    {
        SqlDialect.PostgreSql => "public",
        _ => "dbo"
    };

    // Schema igual ao padrão do dialeto vira nulo, para comparar e mostrar do mesmo jeito.
    public static string? NormalizeSchema(string? schema, SqlDialect dialect)
    {
        var clean = Unquote(schema);
        return string.IsNullOrEmpty(clean) || clean.Equals(DefaultSchema(dialect), StringComparison.OrdinalIgnoreCase) ? null : clean;
    }

    public static string Key(string? schema, string name, SqlDialect dialect) =>
        ((NormalizeSchema(schema, dialect) ?? DefaultSchema(dialect)) + "." + Unquote(name)).ToLowerInvariant();

    public static string Display(string? schema, string name, SqlDialect dialect) =>
        NormalizeSchema(schema, dialect) is { } s ? s + "." + Unquote(name) : Unquote(name);

    // "schema.tabela" ou "tabela", como o texto cita.
    public static string KeyOf(string qualified, SqlDialect dialect)
    {
        var dot = qualified.LastIndexOf('.');
        return dot < 0 ? Key(null, qualified, dialect) : Key(qualified[..dot], qualified[(dot + 1)..], dialect);
    }

    public static string Unquote(string? name) =>
        (name ?? string.Empty).Trim().Trim('[', ']', '"', '`').Trim();

    // "NVARCHAR (30)" e "nvarchar(30)" são o mesmo tipo.
    public static string NormalizeType(string type) =>
        new string(type.Where(c => !char.IsWhiteSpace(c) && c is not '[' and not ']' and not '"').ToArray()).ToLowerInvariant();

    // No PostgreSQL, o mesmo tipo tem vários nomes: o .sql escreve "int", "varchar(100)" e "timestamptz",
    // e o Npgsql e o catálogo escrevem "integer", "character varying(100)" e "timestamp with time zone".
    // Para comparar entre fontes, os sinônimos viram o nome do catálogo (serial vira o inteiro dele).
    // O tipo mostrado continua como cada fonte escreve.
    public static string NormalizeType(string type, SqlDialect dialect)
    {
        if (dialect != SqlDialect.PostgreSql) return NormalizeType(type);
        var text = string.Join(" ", type.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Replace(" (", "(");
        var array = text.EndsWith("[]", StringComparison.Ordinal);
        if (array) text = text[..^2];
        var paren = text.IndexOf('(');
        var (name, args) = paren < 0 ? (text, "") : (text[..paren].Trim(), text[paren..].Replace(" ", ""));
        name = name switch
        {
            "int" or "int4" or "serial" or "serial4" => "integer",
            "int8" or "bigserial" or "serial8" => "bigint",
            "int2" or "smallserial" or "serial2" => "smallint",
            "varchar" => "character varying",
            "char" or "bpchar" => "character",
            "timestamptz" => "timestamp with time zone",
            "timestamp" => "timestamp without time zone",
            "timetz" => "time with time zone",
            "time" => "time without time zone",
            "bool" => "boolean",
            "float8" or "float" => "double precision",
            "float4" => "real",
            "decimal" => "numeric",
            _ => name
        };
        if (name == "character" && args.Length == 0) args = "(1)";
        return NormalizeType(name + args + (array ? "[]" : ""));
    }
}
