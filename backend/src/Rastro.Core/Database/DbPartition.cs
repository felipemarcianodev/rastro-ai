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
}
