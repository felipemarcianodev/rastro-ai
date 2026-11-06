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
}
