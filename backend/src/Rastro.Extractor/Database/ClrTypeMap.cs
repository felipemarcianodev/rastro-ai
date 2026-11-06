using Rastro.Core.Database;

namespace Rastro.Extractor.Database;

// O que o leitor sabe de uma propriedade para deduzir o tipo da coluna por convenção do EF Core.
// ClrType sem o "?" ("string", "int", "DateTime", "byte[]"); EnumUnderlying: o tipo de base do enum ("int" quando não declarado).
internal sealed record ClrColumn(string ClrType, string? EnumUnderlying, int? MaxLength, int? Precision, int? Scale, bool? Unicode, bool IsKeyLike);

// Tipo da coluna por convenção do provedor do EF Core, quando o código não diz o tipo (HasColumnType,
// [Column(TypeName)]). Um mapa por dialeto: o do SQL Server (Fase 37) e o do Npgsql (Fase 42). Tipo que o
// mapa não conhece sai sem tipo e com uma nota, em vez de um tipo errado.
internal static class ClrTypeMap
{
    public static string? Map(SqlDialect dialect, ClrColumn column) => dialect switch
    {
        SqlDialect.SqlServer => SqlServer(column),
        SqlDialect.PostgreSql => PostgreSql(column),
        _ => null
    };

    // Como o Npgsql (EF Core 8 em diante) escreve na migration: o nome do catálogo do PostgreSQL.
    private static string? PostgreSql(ClrColumn c)
    {
        if (c.EnumUnderlying is { } underlying) return PostgreSql(c with { ClrType = underlying, EnumUnderlying = null });
        var simple = Simple(c.ClrType);
        if (simple.EndsWith("[]", StringComparison.Ordinal) && simple != "byte[]")
            return PostgreSql(c with { ClrType = simple[..^2], MaxLength = null }) is { } element ? element + "[]" : null;
        if (simple.StartsWith("List<", StringComparison.Ordinal) && simple.EndsWith('>'))
            return PostgreSql(c with { ClrType = simple[5..^1], MaxLength = null }) is { } item ? item + "[]" : null;
        return simple switch
        {
            "string" or "String" => c.MaxLength is { } max ? $"character varying({max})" : "text",
            "char" or "Char" => "character(1)",
            "int" or "Int32" => "integer",
            "long" or "Int64" => "bigint",
            "short" or "Int16" or "byte" or "Byte" or "sbyte" or "SByte" => "smallint",
            "bool" or "Boolean" => "boolean",
            "decimal" or "Decimal" => c.Precision is { } p ? $"numeric({p},{c.Scale ?? 0})" : "numeric",
            "double" or "Double" => "double precision",
            "float" or "Single" => "real",
            "DateTime" or "DateTimeOffset" => "timestamp with time zone",
            "DateOnly" => "date",
            "TimeOnly" => "time without time zone",
            "TimeSpan" => "interval",
            "Guid" => "uuid",
            "byte[]" => "bytea",
            "JsonDocument" or "JsonElement" => "jsonb",
            _ => null
        };
    }

    private static string? SqlServer(ClrColumn c)
    {
        if (c.EnumUnderlying is { } underlying) return SqlServer(c with { ClrType = underlying, EnumUnderlying = null });
        return Simple(c.ClrType) switch
        {
            "string" or "String" => (c.Unicode == false ? "varchar" : "nvarchar") + "(" + (c.MaxLength?.ToString() ?? (c.IsKeyLike ? (c.Unicode == false ? "900" : "450") : "max")) + ")",
            "char" => "nvarchar(1)",
            "int" or "Int32" => "int",
            "long" or "Int64" => "bigint",
            "short" or "Int16" => "smallint",
            "byte" or "Byte" => "tinyint",
            "bool" or "Boolean" => "bit",
            "decimal" or "Decimal" => $"decimal({c.Precision ?? 18},{c.Scale ?? 2})",
            "double" or "Double" => "float",
            "float" or "Single" => "real",
            "DateTime" => "datetime2",
            "DateTimeOffset" => "datetimeoffset",
            "DateOnly" => "date",
            "TimeOnly" or "TimeSpan" => "time",
            "Guid" => "uniqueidentifier",
            "byte[]" => "varbinary(" + (c.MaxLength?.ToString() ?? (c.IsKeyLike ? "900" : "max")) + ")",
            _ => null
        };
    }

    // "System.String" e "global::System.Guid" viram "String" e "Guid".
    private static string Simple(string type)
    {
        var name = type.Replace("global::", string.Empty);
        if (name.EndsWith("[]", StringComparison.Ordinal)) return name.StartsWith("System.Byte", StringComparison.Ordinal) ? "byte[]" : name;
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }

    public static bool IsValueType(string clrType) =>
        Simple(clrType) is "int" or "Int32" or "long" or "Int64" or "short" or "Int16" or "byte" or "Byte" or "bool" or "Boolean"
            or "decimal" or "Decimal" or "double" or "Double" or "float" or "Single" or "DateTime" or "DateTimeOffset"
            or "DateOnly" or "TimeOnly" or "TimeSpan" or "Guid" or "char";

    public static bool IsIdentityCandidate(string clrType) =>
        Simple(clrType) is "int" or "Int32" or "long" or "Int64" or "short" or "Int16";
}
