using Rastro.Core.Database;

namespace Rastro.Extractor.Database;

// O que o leitor sabe de uma propriedade para deduzir o tipo da coluna por convenção do EF Core.
// ClrType sem o "?" ("string", "int", "DateTime", "byte[]"); EnumUnderlying: o tipo de base do enum ("int" quando não declarado).
internal sealed record ClrColumn(string ClrType, string? EnumUnderlying, int? MaxLength, int? Precision, int? Scale, bool? Unicode, bool IsKeyLike);

// Tipo da coluna por convenção do provedor do EF Core, quando o código não diz o tipo (HasColumnType,
// [Column(TypeName)]). Um mapa por dialeto: o do PostgreSQL entra na Fase 42; até lá, sem mapa, a
// coluna sai sem tipo e com uma nota, em vez de um tipo errado.
internal static class ClrTypeMap
{
    public static string? Map(SqlDialect dialect, ClrColumn column) => dialect switch
    {
        SqlDialect.SqlServer => SqlServer(column),
        _ => null
    };

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
