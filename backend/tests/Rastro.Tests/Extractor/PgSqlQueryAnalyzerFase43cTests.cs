using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-PGQ-031 em diante: o reteste da Fase 43a num sistema real (Fase 43c): coluna residual
// do filtro, booleano como igualdade. Esquema fictício de tarefas e alertas.
public class PgSqlQueryAnalyzerFase43cTests
{
    private static readonly DbSchema Sistema = DbSchemaExtractor.Extract([new SchemaFile("banco/x.sql", """
        CREATE TABLE tarefas (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, space_id uuid NOT NULL, archived_at timestamptz NULL, category text NULL, role text NULL, titulo text NOT NULL);
        CREATE TABLE alertas (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, user_id uuid NOT NULL, sent boolean NOT NULL, alert_date_time timestamptz NOT NULL, resource_id uuid NULL);
        CREATE INDEX ix_tarefas_tenant_space ON tarefas (tenant_id, space_id);
        CREATE INDEX ix_alertas_sent_data ON alertas (sent, alert_date_time);
        CREATE INDEX ix_alertas_tenant_user ON alertas (tenant_id, user_id);
        """)]);

    private static List<QueryFinding> Missing(string sql) =>
        new PgSqlQueryAnalyzer().Analyze(sql, Sistema).Findings.Where(f => f.Kind == QueryFindingKind.ColumnWithoutIndex).ToList();

    [Fact] // EXT-PGQ-031
    public void Analyze_ColunaResidualDepoisDeIndiceUsado_NaoViraColunaSemIndice()
    {
        // O índice (tenant_id, space_id) busca as linhas; archived_at e category são conferidas nelas.
        Assert.Empty(Missing("SELECT t.id FROM tarefas t WHERE t.tenant_id = @t AND t.space_id = @s AND t.archived_at IS NULL LIMIT 10"));
        Assert.Empty(Missing("SELECT t.id FROM tarefas t WHERE t.tenant_id = @t AND t.space_id = @s AND t.category = @c LIMIT 10"));
        Assert.Empty(Missing("SELECT a.id FROM alertas a WHERE a.tenant_id = @t AND a.user_id = @u AND a.resource_id = @r LIMIT 10"));
        // Só o tenant bate no índice: space_id e role são residuais.
        Assert.Empty(Missing("SELECT t.id FROM tarefas t WHERE t.tenant_id = @t AND t.role = @r LIMIT 10"));
    }

    [Fact] // EXT-PGQ-032
    public void Analyze_NenhumIndiceAtendeOFiltro_ContinuaAcusando()
    {
        // category e role não começam índice nenhum e nenhuma coluna do filtro atende um: os dois são achados.
        var sql = "SELECT t.id FROM tarefas t WHERE t.category = @c AND t.role = @r LIMIT 10";

        Assert.Equal(["t.category", "t.role"], Missing(sql).Select(f => sql.Substring(f.Start, f.Length)));
        // A segunda coluna do índice sem a primeira não atende.
        Assert.Single(Missing("SELECT t.id FROM tarefas t WHERE t.space_id = @s LIMIT 10"));
    }

    [Fact] // EXT-PGQ-033
    public void Analyze_ColunaResidualNaJuncao_CadaTabelaPorSi()
    {
        // O ON liga alertas a tarefas por colunas sem índice em alertas (resource_id), e o filtro de tarefas
        // usa o índice composto: o residual da tarefa some, o da junção em alertas continua.
        var sql = "SELECT a.id FROM tarefas t JOIN alertas a ON a.resource_id = t.id WHERE t.tenant_id = @t AND t.space_id = @s AND t.archived_at IS NULL LIMIT 10";

        var found = Missing(sql);

        Assert.Equal(["alertas"], found.Select(f => f.Table));
        Assert.Contains("a.resource_id", sql.Substring(found[0].Start, found[0].Length));
        // Na junção com o ON usando o índice composto, a coluna residual do ON some.
        Assert.Empty(Missing("SELECT a.id FROM tarefas t JOIN alertas a ON a.tenant_id = t.tenant_id AND a.user_id = t.id AND a.resource_id = t.id WHERE t.id = @i LIMIT 10"));
    }

    [Theory] // EXT-PGQ-034
    [InlineData("WHERE NOT (a.sent) AND a.alert_date_time <= now()")]
    [InlineData("WHERE NOT a.sent AND a.alert_date_time <= now()")]
    [InlineData("WHERE a.sent AND a.alert_date_time <= now()")]
    [InlineData("WHERE a.sent = true AND a.alert_date_time <= now()")]
    [InlineData("WHERE a.sent = false AND a.alert_date_time <= now()")]
    [InlineData("WHERE a.sent IS NOT TRUE AND a.alert_date_time <= now()")]
    [InlineData("WHERE a.sent IS FALSE AND a.alert_date_time <= now()")]
    public void Analyze_BooleanoNoFiltro_ContaComoIgualdadeNoIndiceComposto(string where)
    {
        var sql = $"SELECT a.id FROM alertas a {where} LIMIT 10";

        Assert.Empty(Missing(sql));
    }
}
