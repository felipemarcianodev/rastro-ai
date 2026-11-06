using System.Text;
using Microsoft.Data.SqlClient;
using Rastro.Core.Database;

namespace Rastro.Infrastructure.Database.Live;

// O que o Rastro pede a um banco vivo (Fase 40b, ADR-006). Só três coisas, todas só de leitura: o
// catálogo, o plano estimado de uma consulta já conferida pelo TSqlPlanGuard e a contagem de tabelas
// para o teste da conexão. Nunca os dados das tabelas.
public interface ILiveDatabase
{
    Task<SchemaFragment> ReadCatalogAsync(string connectionString, CancellationToken ct);

    // sql: a consulta conferida; variables: as variáveis dela, declaradas como texto antes do pedido.
    Task<string> ShowPlanXmlAsync(string connectionString, string sql, IReadOnlyList<string> variables, CancellationToken ct);

    Task<int> CountTablesAsync(string connectionString, CancellationToken ct);
}

// SQL Server pelo driver oficial (Microsoft.Data.SqlClient). Os comandos ao catálogo são constantes,
// sem texto nenhum da requisição. Toda conexão tem tempo limite, sem pool (o SET SHOWPLAN_XML de uma
// conexão nunca passa para a seguinte) e com o nome "Rastro (só leitura)" para o DBA ver quem é.
public sealed class SqlServerLiveDatabase : ILiveDatabase
{
    public const int ConnectTimeoutSeconds = 10;
    public const int CommandTimeoutSeconds = 30;
    private static readonly SchemaSource Live = new(SchemaSourceKind.LiveDatabase, "banco vivo", 0);

    private const string ColumnsSql = """
        SELECT s.name, t.name, c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, dc.definition
        FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.columns c ON c.object_id = t.object_id
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
        WHERE t.is_ms_shipped = 0
        ORDER BY s.name, t.name, c.column_id
        """;

    private const string IndexesSql = """
        SELECT s.name, t.name, i.name, i.is_primary_key, i.is_unique, ic.is_included_column, c.name
        FROM sys.indexes i
        JOIN sys.tables t ON t.object_id = i.object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE t.is_ms_shipped = 0 AND i.type > 0
        ORDER BY s.name, t.name, i.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id
        """;

    private const string ForeignKeysSql = """
        SELECT s.name, t.name, fk.name, rs.name, rt.name, pc.name, rc.name, fk.delete_referential_action
        FROM sys.foreign_keys fk
        JOIN sys.tables t ON t.object_id = fk.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
        JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        ORDER BY s.name, t.name, fk.name, fkc.constraint_column_id
        """;

    private const string ViewsSql = """
        SELECT s.name, v.name, c.name
        FROM sys.views v
        JOIN sys.schemas s ON s.schema_id = v.schema_id
        LEFT JOIN sys.columns c ON c.object_id = v.object_id
        WHERE v.is_ms_shipped = 0
        ORDER BY s.name, v.name, c.column_id
        """;

    private const string ProceduresSql = """
        SELECT s.name, p.name, pa.name, ty.name, pa.max_length, pa.precision, pa.scale, pa.is_output
        FROM sys.procedures p
        JOIN sys.schemas s ON s.schema_id = p.schema_id
        LEFT JOIN sys.parameters pa ON pa.object_id = p.object_id AND pa.parameter_id > 0
        LEFT JOIN sys.types ty ON ty.user_type_id = pa.user_type_id
        WHERE p.is_ms_shipped = 0
        ORDER BY s.name, p.name, pa.parameter_id
        """;

    private const string CountSql = "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0";

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
            var key = DbNames.Key(schema, name, SqlDialect.SqlServer);
            if (!tables.TryGetValue(key, out var table))
            {
                tables[key] = table = new TableRows(schema, name);
                order.Add(key);
            }
            return table;
        }

        await foreach (var r in RowsAsync(connection, ColumnsSql, ct))
            Table(r.GetString(0), r.GetString(1)).Columns.Add(new DbColumn(
                r.GetString(2),
                TypeText(r.GetString(3), r.GetInt16(4), r.GetByte(5), r.GetByte(6)),
                r.GetBoolean(7),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.GetBoolean(8),
                [Live]));

        var indexes = new Dictionary<(string Table, string Index), (bool Pk, bool Unique, List<string> Keys, List<string> Included)>();
        await foreach (var r in RowsAsync(connection, IndexesSql, ct))
        {
            var table = Table(r.GetString(0), r.GetString(1));
            var id = (DbNames.Key(table.Schema, table.Name, SqlDialect.SqlServer), r.IsDBNull(2) ? "" : r.GetString(2));
            if (!indexes.TryGetValue(id, out var index))
            {
                indexes[id] = index = (r.GetBoolean(3), r.GetBoolean(4), [], []);
                table.IndexOrder.Add(id.Item2);
            }
            (r.GetBoolean(5) ? index.Included : index.Keys).Add(r.GetString(6));
        }
        foreach (var ((tableKey, name), index) in indexes)
        {
            var table = tables[tableKey];
            if (index.Pk) table.PrimaryKey = new DbPrimaryKey(name, index.Keys, Live);
            else table.Indexes.Add(new DbIndex(name, index.Keys, index.Included, index.Unique, Live));
        }

        var foreignKeys = new Dictionary<(string Table, string Name), (string Principal, List<string> Columns, List<string> PrincipalColumns, DeleteAction OnDelete)>();
        var fkOrder = new List<(string, string)>();
        await foreach (var r in RowsAsync(connection, ForeignKeysSql, ct))
        {
            var table = Table(r.GetString(0), r.GetString(1));
            var id = (DbNames.Key(table.Schema, table.Name, SqlDialect.SqlServer), r.GetString(2));
            if (!foreignKeys.TryGetValue(id, out var fk))
            {
                foreignKeys[id] = fk = (DbNames.Display(r.GetString(3), r.GetString(4), SqlDialect.SqlServer), [], [], r.GetByte(7) switch
                {
                    1 => DeleteAction.Cascade,
                    2 => DeleteAction.SetNull,
                    3 => DeleteAction.SetDefault,
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

        var views = new List<DbView>();
        await foreach (var r in RowsAsync(connection, ViewsSql, ct))
        {
            var (schema, name) = (r.GetString(0), r.GetString(1));
            var last = views.Count > 0 ? views[^1] : null;
            if (last is null || last.Name != name || last.Schema != NullIfDefault(schema))
                views.Add(last = new DbView(NullIfDefault(schema), name, new List<string>(), [], Live));
            if (!r.IsDBNull(2)) ((List<string>)last.Columns).Add(r.GetString(2));
        }
        for (var i = 0; i < views.Count; i++) views[i] = views[i] with { Columns = views[i].Columns.ToList() };

        var procedures = new List<DbProcedure>();
        await foreach (var r in RowsAsync(connection, ProceduresSql, ct))
        {
            var (schema, name) = (r.GetString(0), r.GetString(1));
            var last = procedures.Count > 0 ? procedures[^1] : null;
            if (last is null || last.Name != name || last.Schema != NullIfDefault(schema))
                procedures.Add(last = new DbProcedure(NullIfDefault(schema), name, new List<DbParameter>(), [], [], Live));
            if (!r.IsDBNull(2))
                ((List<DbParameter>)last.Parameters).Add(new DbParameter(r.GetString(2), TypeText(r.GetString(3), r.GetInt16(4), r.GetByte(5), r.GetByte(6)), r.GetBoolean(7)));
        }

        var built = order.Select(k => tables[k]).Select(t => new DbTable(NullIfDefault(t.Schema), t.Name, t.Columns, t.PrimaryKey,
            t.ForeignKeys, t.Indexes, [Live])).ToList();
        return new SchemaFragment(built, views, procedures, []);
    }

    public async Task<string> ShowPlanXmlAsync(string connectionString, string sql, IReadOnlyList<string> variables, CancellationToken ct)
    {
        await using var connection = await OpenAsync(connectionString, ct);
        await using (var on = Command(connection, "SET SHOWPLAN_XML ON"))
            await on.ExecuteNonQueryAsync(ct);

        // Com o SHOWPLAN_XML ligado, o banco compila e devolve o plano, sem executar nada do lote. As
        // variáveis da consulta entram declaradas como texto (os nomes já conferidos pelo TSqlPlanGuard).
        var batch = new StringBuilder();
        foreach (var variable in variables) batch.Append("DECLARE ").Append(variable).AppendLine(" nvarchar(4000);");
        batch.Append(sql);

        var xml = new StringBuilder();
        await using (var command = Command(connection, batch.ToString()))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            do
            {
                while (await reader.ReadAsync(ct))
                    if (!reader.IsDBNull(0)) xml.Append(reader.GetString(0)).Append('\n');
            } while (await reader.NextResultAsync(ct));
        }
        // Sem pool, a conexão fecha aqui e o SHOWPLAN_XML vai junto.
        return xml.ToString();
    }

    private static async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            ApplicationName = "Rastro (só leitura)",
            ApplicationIntent = ApplicationIntent.ReadOnly
        };
        if (builder.ConnectTimeout <= 0 || builder.ConnectTimeout > ConnectTimeoutSeconds) builder.ConnectTimeout = ConnectTimeoutSeconds;
        var connection = new SqlConnection(builder.ConnectionString);
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

    private static SqlCommand Command(SqlConnection connection, string text) =>
        new(text, connection) { CommandTimeout = CommandTimeoutSeconds };

    private static async IAsyncEnumerable<SqlDataReader> RowsAsync(SqlConnection connection, string sql, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var command = Command(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) yield return reader;
    }

    private static string? NullIfDefault(string schema) => DbNames.NormalizeSchema(schema, SqlDialect.SqlServer);

    // Como o código escreve: "nvarchar(30)", "decimal(10,2)", "nvarchar(max)", "datetime2" (precisão padrão).
    public static string TypeText(string type, short maxLength, byte precision, byte scale) => type.ToLowerInvariant() switch
    {
        "nvarchar" or "nchar" => $"{type}({(maxLength < 0 ? "max" : (maxLength / 2).ToString())})",
        "varchar" or "char" or "varbinary" or "binary" => $"{type}({(maxLength < 0 ? "max" : maxLength.ToString())})",
        "decimal" or "numeric" => $"{type}({precision},{scale})",
        "datetime2" or "datetimeoffset" or "time" => scale == 7 ? type : $"{type}({scale})",
        _ => type
    };

    private sealed class TableRows(string schema, string name)
    {
        public string Schema { get; } = schema;
        public string Name { get; } = name;
        public List<DbColumn> Columns { get; } = [];
        public DbPrimaryKey? PrimaryKey { get; set; }
        public List<DbForeignKey> ForeignKeys { get; } = [];
        public List<DbIndex> Indexes { get; } = [];
        public List<string> IndexOrder { get; } = [];
    }
}
