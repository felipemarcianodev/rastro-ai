using Npgsql;
using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Infrastructure.Database.Live;

// PostgreSQL pelo driver oficial (Npgsql), Fase 42b (ADR-006). As mesmas garantias do SQL Server: os
// comandos ao catálogo (pg_catalog) são constantes, sem texto nenhum da requisição; toda conexão tem
// tempo limite, não entra em pool, se identifica como "Rastro (leitura)" e abre com
// default_transaction_read_only ligado. O plano é o EXPLAIN sem ANALYZE (nunca executa a instrução),
// dentro de uma transação só de leitura que sempre volta atrás, e vai num lote de um comando só: o
// protocolo estendido do PostgreSQL não aceita duas instruções no mesmo comando. Nunca os dados das tabelas.
public sealed class NpgsqlLiveDatabase : ILiveDatabase
{
    public const int ConnectTimeoutSeconds = 10;
    public const int CommandTimeoutSeconds = 30;

    // O plano genérico da consulta com parâmetro (plan_cache_mode = force_generic_plan) existe desde o
    // PostgreSQL 12.
    public const int GenericPlanMajorVersion = 12;

    private static readonly SchemaSource Live = new(SchemaSourceKind.LiveDatabase, "banco vivo", 0);

    // Os schemas do próprio PostgreSQL ficam de fora.
    private const string UserSchemas = "n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg\\_toast%' AND n.nspname NOT LIKE 'pg\\_temp%'";

    private const string ColumnsSql = $"""
        SELECT n.nspname, c.relname, a.attname, format_type(a.atttypid, a.atttypmod), NOT a.attnotnull,
               pg_get_expr(d.adbin, d.adrelid), (a.attidentity <> '' OR coalesce(pg_get_expr(d.adbin, d.adrelid), '') LIKE 'nextval(%')
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
        LEFT JOIN pg_attrdef d ON d.adrelid = c.oid AND d.adnum = a.attnum
        WHERE c.relkind IN ('r', 'p') AND NOT c.relispartition AND {UserSchemas}
        ORDER BY n.nspname, c.relname, a.attnum
        """;

    private const string IndexesSql = $"""
        SELECT n.nspname, t.relname, i.relname, x.indisprimary, x.indisunique, k.ord > x.indnkeyatts,
               coalesce(a.attname, pg_get_indexdef(x.indexrelid, k.ord::int, true))
        FROM pg_index x
        JOIN pg_class i ON i.oid = x.indexrelid
        JOIN pg_class t ON t.oid = x.indrelid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        CROSS JOIN LATERAL unnest(x.indkey::int2[]) WITH ORDINALITY AS k(attnum, ord)
        LEFT JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum AND k.attnum > 0
        WHERE t.relkind IN ('r', 'p') AND NOT t.relispartition AND {UserSchemas}
        ORDER BY n.nspname, t.relname, i.relname, k.ord
        """;

    private const string ForeignKeysSql = $"""
        SELECT n.nspname, t.relname, co.conname, rn.nspname, rt.relname, a.attname, ra.attname, co.confdeltype::text
        FROM pg_constraint co
        JOIN pg_class t ON t.oid = co.conrelid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        JOIN pg_class rt ON rt.oid = co.confrelid
        JOIN pg_namespace rn ON rn.oid = rt.relnamespace
        CROSS JOIN LATERAL unnest(co.conkey, co.confkey) WITH ORDINALITY AS k(col, refcol, ord)
        JOIN pg_attribute a ON a.attrelid = co.conrelid AND a.attnum = k.col
        JOIN pg_attribute ra ON ra.attrelid = co.confrelid AND ra.attnum = k.refcol
        WHERE co.contype = 'f' AND {UserSchemas}
        ORDER BY n.nspname, t.relname, co.conname, k.ord
        """;

    private const string ViewsSql = $"""
        SELECT n.nspname, c.relname, a.attname
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        LEFT JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
        WHERE c.relkind IN ('v', 'm') AND {UserSchemas}
        ORDER BY n.nspname, c.relname, a.attnum
        """;

    // Funções e procedures do usuário; as que vêm de uma extensão (pgcrypto, por exemplo) ficam de fora.
    private const string ProceduresSql = $"""
        SELECT n.nspname, p.proname, p.oid::bigint, k.ord, p.proargnames[k.ord], format_type(k.typ, NULL), coalesce(p.proargmodes[k.ord]::text, 'i')
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        LEFT JOIN LATERAL unnest(coalesce(p.proallargtypes, p.proargtypes::oid[])) WITH ORDINALITY AS k(typ, ord) ON true
        WHERE p.prokind IN ('f', 'p') AND {UserSchemas}
          AND NOT EXISTS (SELECT 1 FROM pg_depend dep WHERE dep.classid = 'pg_proc'::regclass AND dep.objid = p.oid AND dep.deptype = 'e')
        ORDER BY n.nspname, p.proname, p.oid, k.ord
        """;

    // A estimativa de linhas é do catálogo (reltuples, mantida pelo VACUUM e pelo ANALYZE): nunca um
    // count(*), que leria a tabela. -1 (nunca analisada) e 0 ficam como desconhecido.
    private const string RowEstimatesSql = $"""
        SELECT n.nspname, c.relname, c.reltuples::bigint
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p') AND NOT c.relispartition AND {UserSchemas}
        """;

    private const string CountSql = $"""
        SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p') AND NOT c.relispartition AND {UserSchemas}
        """;

    public SqlDialect Dialect => SqlDialect.PostgreSql;

    public async Task<int> CountTablesAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = await OpenAsync(connectionString, ct);
        await using var command = Command(connection, CountSql);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    public async Task<SchemaFragment> ReadCatalogAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = await OpenAsync(connectionString, ct);
        var tables = new Dictionary<string, TableRows>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        TableRows Table(string schema, string name)
        {
            var key = DbNames.Key(schema, name, SqlDialect.PostgreSql);
            if (!tables.TryGetValue(key, out var table))
            {
                tables[key] = table = new TableRows(schema, name);
                order.Add(key);
            }
            return table;
        }

        await foreach (var r in RowsAsync(connection, ColumnsSql, ct))
            Table(r.GetString(0), r.GetString(1)).Columns.Add(new DbColumn(
                r.GetString(2), r.GetString(3), r.GetBoolean(4), r.IsDBNull(5) ? null : r.GetString(5), r.GetBoolean(6), [Live]));

        var indexes = new Dictionary<(string Table, string Index), (bool Pk, bool Unique, List<string> Keys, List<string> Included)>();
        var indexOrder = new List<(string, string)>();
        await foreach (var r in RowsAsync(connection, IndexesSql, ct))
        {
            var table = Table(r.GetString(0), r.GetString(1));
            var id = (DbNames.Key(table.Schema, table.Name, SqlDialect.PostgreSql), r.GetString(2));
            if (!indexes.TryGetValue(id, out var index))
            {
                indexes[id] = index = (r.GetBoolean(3), r.GetBoolean(4), [], []);
                indexOrder.Add(id);
            }
            (r.GetBoolean(5) ? index.Included : index.Keys).Add(r.GetString(6));
        }
        foreach (var id in indexOrder)
        {
            var index = indexes[id];
            var table = tables[id.Item1];
            if (index.Pk) table.PrimaryKey = new DbPrimaryKey(id.Item2, index.Keys, Live);
            else table.Indexes.Add(new DbIndex(id.Item2, index.Keys, index.Included, index.Unique, Live));
        }

        var foreignKeys = new Dictionary<(string Table, string Name), (string Principal, List<string> Columns, List<string> PrincipalColumns, DeleteAction OnDelete)>();
        var fkOrder = new List<(string, string)>();
        await foreach (var r in RowsAsync(connection, ForeignKeysSql, ct))
        {
            var table = Table(r.GetString(0), r.GetString(1));
            var id = (DbNames.Key(table.Schema, table.Name, SqlDialect.PostgreSql), r.GetString(2));
            if (!foreignKeys.TryGetValue(id, out var fk))
            {
                foreignKeys[id] = fk = (DbNames.Display(r.GetString(3), r.GetString(4), SqlDialect.PostgreSql), [], [], r.GetString(7) switch
                {
                    "c" => DeleteAction.Cascade,
                    "n" => DeleteAction.SetNull,
                    "d" => DeleteAction.SetDefault,
                    "r" => DeleteAction.Restrict,
                    _ => DeleteAction.NoAction
                });
                fkOrder.Add(id);
            }
            fk.Columns.Add(r.GetString(5));
            fk.PrincipalColumns.Add(r.GetString(6));
        }
        foreach (var id in fkOrder)
        {
            var fk = foreignKeys[id];
            tables[id.Item1].ForeignKeys.Add(new DbForeignKey(id.Item2, fk.Columns, fk.Principal, fk.PrincipalColumns, fk.OnDelete, Live));
        }

        await foreach (var r in RowsAsync(connection, RowEstimatesSql, ct))
            if (!r.IsDBNull(2) && r.GetInt64(2) > 0)
                Table(r.GetString(0), r.GetString(1)).EstimatedRows = r.GetInt64(2);

        var views = new List<(string? Schema, string Name, List<string> Columns)>();
        await foreach (var r in RowsAsync(connection, ViewsSql, ct))
        {
            var (schema, name) = (NullIfDefault(r.GetString(0)), r.GetString(1));
            if (views.Count == 0 || views[^1].Name != name || views[^1].Schema != schema) views.Add((schema, name, []));
            if (!r.IsDBNull(2)) views[^1].Columns.Add(r.GetString(2));
        }

        var procedures = new List<(string? Schema, string Name, long Oid, List<DbParameter> Parameters)>();
        await foreach (var r in RowsAsync(connection, ProceduresSql, ct))
        {
            var (schema, name, oid) = (NullIfDefault(r.GetString(0)), r.GetString(1), r.GetInt64(2));
            if (procedures.Count == 0 || procedures[^1].Oid != oid) procedures.Add((schema, name, oid, []));
            if (r.IsDBNull(3)) continue;
            var position = r.GetInt64(3);
            var mode = r.GetString(6);
            procedures[^1].Parameters.Add(new DbParameter(r.IsDBNull(4) || r.GetString(4).Length == 0 ? $"${position}" : r.GetString(4),
                r.IsDBNull(5) ? string.Empty : r.GetString(5), mode is "o" or "b" or "t"));
        }

        var built = order.Select(k => tables[k]).Select(t => new DbTable(NullIfDefault(t.Schema), t.Name, t.Columns, t.PrimaryKey,
            t.ForeignKeys, t.Indexes, [Live], null, t.EstimatedRows)).ToList();
        return new SchemaFragment(built,
            views.Select(v => new DbView(v.Schema, v.Name, v.Columns, [], Live)).ToList(),
            procedures.DistinctBy(p => (p.Schema, p.Name)).Select(p => new DbProcedure(p.Schema, p.Name, p.Parameters, [], [], Live)).ToList(),
            []);
    }

    public async Task<string> EstimatedPlanAsync(string connectionString, string sql, IReadOnlyList<string> variables, CancellationToken ct)
    {
        await using var connection = await OpenAsync(connectionString, ct);
        var (text, parameters) = PgSqlPlanGuard.Positional(sql);
        if (parameters > 0 && connection.PostgreSqlVersion.Major < GenericPlanMajorVersion)
            throw new LivePlanUnavailableException($"o PostgreSQL {connection.PostgreSqlVersion.Major} só dá o plano de consulta com parâmetro a partir da versão 12: troque os parâmetros por valores");

        // Transação só de leitura que sempre volta atrás; o EXPLAIN sem ANALYZE não executa a instrução.
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var readOnly = Command(connection, "SET TRANSACTION READ ONLY"))
        {
            readOnly.Transaction = transaction;
            await readOnly.ExecuteNonQueryAsync(ct);
        }

        // Com parâmetro, a consulta vira um PREPARE e o plano é o genérico (o que não depende do valor),
        // pedido pelo EXPLAIN EXECUTE com nulos: sem ANALYZE, nada é executado.
        var explainText = "EXPLAIN (FORMAT JSON) " + text;
        if (parameters > 0)
        {
            await RunAsync(connection, transaction, "SET LOCAL plan_cache_mode = force_generic_plan", ct);
            await RunAsync(connection, transaction, "PREPARE rastro_plano AS " + text, ct);
            explainText = $"EXPLAIN (FORMAT JSON) EXECUTE rastro_plano({string.Join(", ", Enumerable.Repeat("NULL", parameters))})";
        }

        // Um lote de um comando só: sem reescrita do Npgsql, o protocolo estendido recusa duas instruções.
        await using var batch = new NpgsqlBatch(connection, transaction) { Timeout = CommandTimeoutSeconds };
        batch.BatchCommands.Add(new NpgsqlBatchCommand(explainText));
        string plan;
        await using (var reader = await batch.ExecuteReaderAsync(ct))
        {
            var json = new System.Text.StringBuilder();
            while (await reader.ReadAsync(ct))
                if (!reader.IsDBNull(0)) json.Append(reader.GetString(0));
            plan = json.ToString();
        }
        await transaction.RollbackAsync(ct);
        return plan;
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString, CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            Multiplexing = false,
            ApplicationName = "Rastro (leitura)",
            CommandTimeout = CommandTimeoutSeconds,
            // Só de leitura desde a abertura, e o tempo limite também do lado do banco. Substitui o que a
            // string trouxer em Options.
            Options = $"-c default_transaction_read_only=on -c statement_timeout={CommandTimeoutSeconds * 1000}"
        };
        if (builder.Timeout <= 0 || builder.Timeout > ConnectTimeoutSeconds) builder.Timeout = ConnectTimeoutSeconds;
        var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    // Um comando num lote de um só (o texto da consulta vai no PREPARE: duas instruções são recusadas).
    private static async Task RunAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string text, CancellationToken ct)
    {
        await using var batch = new NpgsqlBatch(connection, transaction) { Timeout = CommandTimeoutSeconds };
        batch.BatchCommands.Add(new NpgsqlBatchCommand(text));
        await batch.ExecuteNonQueryAsync(ct);
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string text) =>
        new(text, connection) { CommandTimeout = CommandTimeoutSeconds };

    private static async IAsyncEnumerable<NpgsqlDataReader> RowsAsync(NpgsqlConnection connection, string sql, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var command = Command(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) yield return reader;
    }

    private static string? NullIfDefault(string schema) => DbNames.NormalizeSchema(schema, SqlDialect.PostgreSql);

    private sealed class TableRows(string schema, string name)
    {
        public string Schema { get; } = schema;
        public string Name { get; } = name;
        public List<DbColumn> Columns { get; } = [];
        public DbPrimaryKey? PrimaryKey { get; set; }
        public List<DbForeignKey> ForeignKeys { get; } = [];
        public List<DbIndex> Indexes { get; } = [];
        public long? EstimatedRows { get; set; }
    }
}
