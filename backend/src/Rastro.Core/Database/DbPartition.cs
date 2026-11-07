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
    public static bool IsImplicitForeignKey(HashSet<string> tableKeys, string column) =>
        ImplicitTargets(column).Any(tableKeys.Contains);

    // Daqui para cima uma tabela é entidade principal: apontada, por chave estrangeira declarada ou implícita, por
    // pelo menos duas outras tabelas (tarefas, apontada por subtarefas, comentários e atividades). A lista dela
    // filtrada por um pai (o espaço) tende a crescer, então a exceção da lista filha não vale (Fase 44).
    public const int MainEntityReferrers = 2;

    // Quantas outras tabelas apontam para cada tabela do esquema (chave: o nome sem underscore e em minúsculas).
    // A coluna de partição (o tenant) não conta, senão toda tabela apontaria para a do tenant.
    public static Dictionary<string, int> ReferrerCounts(DbSchema? schema, HashSet<string> partition)
    {
        var counts = new Dictionary<string, int>();
        foreach (var table in schema?.Tables ?? [])
        {
            var targets = new HashSet<string>();
            foreach (var fk in table.ForeignKeys)
                if (fk.Columns.Any(c => !partition.Contains(DbNames.Unquote(c))))
                    targets.Add(Flat(fk.PrincipalTable[(fk.PrincipalTable.LastIndexOf('.') + 1)..]));
            foreach (var column in table.Columns)
                if (!partition.Contains(DbNames.Unquote(column.Name)))
                    targets.UnionWith(ImplicitTargets(column.Name));
            targets.Remove(Flat(table.Name));
            foreach (var target in targets) counts[target] = counts.GetValueOrDefault(target) + 1;
        }
        return counts;
    }

    public static int Referrers(Dictionary<string, int> counts, string table) => counts.GetValueOrDefault(Flat(table));

    // Os nomes de tabela a que "x_id" (ou "xId") pode apontar: x, xs, xes e xies.
    private static IEnumerable<string> ImplicitTargets(string column)
    {
        var name = DbNames.Unquote(column);
        string? baseName = null;
        if (name.EndsWith("_id", StringComparison.OrdinalIgnoreCase)) baseName = name[..^3];
        else if (name.Length > 2 && name.EndsWith("Id", StringComparison.Ordinal) && char.IsLower(name[^3])) baseName = name[..^2];
        if (string.IsNullOrEmpty(baseName)) yield break;
        var flat = Flat(baseName);
        if (flat.Length == 0) yield break;
        yield return flat;
        yield return flat + "s";
        yield return flat + "es";
        if (flat.EndsWith('y')) yield return flat[..^1] + "ies";
    }

    private static string Flat(string name) => DbNames.Unquote(name).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
}
