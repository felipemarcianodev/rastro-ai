namespace Rastro.Core.Database;

// A chave de partição do esquema (o tenant, a empresa): a coluna cujo nome aparece em pelo menos metade das
// tabelas (e em pelo menos duas). Filtrar só por ela não limita o resultado a um pai pequeno; filtrar por uma chave estrangeira que
// não é de partição (a tarefa de uma lista de subtarefas) é uma lista filha. Só tabelas contam, views não.
public static class DbPartition
{
    public static HashSet<string> Columns(DbSchema? schema)
    {
        var tables = schema?.Tables ?? [];
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tables.Count == 0) return keys;
        var counts = tables
            .SelectMany(t => t.Columns.Select(c => DbNames.Unquote(c.Name)).Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase);
        foreach (var group in counts)
            // Uma coluna que só uma tabela tem nunca é de partição, mesmo num esquema de duas tabelas.
            if (group.Count() >= 2 && group.Count() * 2 >= tables.Count) keys.Add(group.Key);
        return keys;
    }

    // Os nomes das tabelas do esquema sem underscore e em minúsculas, para conferir a chave estrangeira implícita.
    public static HashSet<string> TableKeys(DbSchema? schema) =>
        (schema?.Tables ?? []).Select(t => Flat(t.Name)).ToHashSet();

    // Chave estrangeira implícita: o sistema não declara a FK, mas a coluna "x_id" (ou "xId") aponta para a
    // tabela "x", "xs", "xes" ou "xies" (task_item_id aponta para task_items). Só o nome conta, não exige índice.
    public static bool IsImplicitForeignKey(HashSet<string> tableKeys, string column)
    {
        var name = DbNames.Unquote(column);
        string? baseName = null;
        if (name.EndsWith("_id", StringComparison.OrdinalIgnoreCase)) baseName = name[..^3];
        else if (name.Length > 2 && name.EndsWith("Id", StringComparison.Ordinal) && char.IsLower(name[^3])) baseName = name[..^2];
        if (string.IsNullOrEmpty(baseName)) return false;
        var flat = Flat(baseName);
        if (flat.Length == 0) return false;
        return tableKeys.Contains(flat) || tableKeys.Contains(flat + "s") || tableKeys.Contains(flat + "es")
            || (flat.EndsWith('y') && tableKeys.Contains(flat[..^1] + "ies"));
    }

    private static string Flat(string name) => DbNames.Unquote(name).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
}
