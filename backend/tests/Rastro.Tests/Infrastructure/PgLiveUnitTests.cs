using Rastro.Core.Database;
using Rastro.Extractor.Database;
using Rastro.Infrastructure.Database.Live;

namespace Rastro.Tests.Infrastructure;

// Cenários INF-PGL-001 a 003 e EXT-PGP-003: o banco vivo do PostgreSQL sem banco nenhum (Fase 42b): o
// dialeto tirado da string de conexão, a leitura do EXPLAIN e o @nome que vira $n para o EXPLAIN.
public class PgLiveUnitTests
{
    [Theory] // INF-PGL-001
    [InlineData("Host=db.exemplo.test;Database=loja;Username=leitura;Password=x", SqlDialect.SqlServer, SqlDialect.PostgreSql)]
    [InlineData("Server=db.exemplo.test;Port=5433;Database=loja;User Id=leitura;Password=x", SqlDialect.SqlServer, SqlDialect.PostgreSql)]
    [InlineData("Data Source=db.exemplo.test;Initial Catalog=loja;User Id=leitura;Password=x", SqlDialect.PostgreSql, SqlDialect.SqlServer)]
    [InlineData("Server=db.exemplo.test;Database=loja;User Id=leitura;Password=x;TrustServerCertificate=True", SqlDialect.PostgreSql, SqlDialect.SqlServer)]
    // A string que serve aos dois drivers fica com o dialeto do repositório.
    [InlineData("Server=db.exemplo.test;Database=loja;User Id=leitura;Password=x", SqlDialect.PostgreSql, SqlDialect.PostgreSql)]
    [InlineData("Server=db.exemplo.test;Database=loja;User Id=leitura;Password=x", SqlDialect.SqlServer, SqlDialect.SqlServer)]
    [InlineData("postgresql://leitura:x@db.exemplo.test/loja", SqlDialect.SqlServer, SqlDialect.PostgreSql)]
    public void Dialeto_DaStringDeConexao_PelasChavesOuPeloRepositorio(string connection, SqlDialect repository, SqlDialect expected)
    {
        Assert.Equal(expected, LiveConnectionDialect.Detect(connection, repository));
    }

    [Fact] // INF-PGL-002
    public void ExplainPlanReader_CustoProprioPercentualEVarredura()
    {
        const string json = """
            [{"Plan": {"Node Type": "Sort", "Total Cost": 20.0, "Plan Rows": 100,
              "Plans": [{"Node Type": "Hash Join", "Join Type": "Inner", "Total Cost": 15.0, "Plan Rows": 100,
                "Plans": [
                  {"Node Type": "Seq Scan", "Relation Name": "pedidos", "Total Cost": 10.0, "Plan Rows": 1000},
                  {"Node Type": "Index Scan", "Relation Name": "clientes", "Index Name": "pk_clientes", "Total Cost": 4.0, "Plan Rows": 1}]}]}}]
            """;

        var plan = ExplainPlanReader.Read(json);

        Assert.Equal(20.0, plan.TotalCost);
        Assert.Equal(["Seq Scan", "Sort", "Index Scan", "Hash Join"], plan.Operations.Select(o => o.PhysicalOp));
        var scan = plan.Operations[0];
        Assert.Equal(("pedidos", 10.0, 50.0, true), (scan.Object, scan.Cost, scan.Percent, scan.Scan));
        Assert.Equal(("clientes.pk_clientes", false), (plan.Operations[2].Object, plan.Operations[2].Scan));
        Assert.Equal(("Inner Join", 1.0), (plan.Operations[3].LogicalOp, plan.Operations[3].Cost));
        Assert.Empty(plan.MissingIndexes);
        Assert.Throws<System.Text.Json.JsonException>(() => ExplainPlanReader.Read("[{}]"));
    }

    [Fact] // EXT-PGP-003
    public void Positional_OArrobaViraDolarDepoisDoMaiorJaUsadoSemMexerEmString()
    {
        var (sql, count) = PgSqlPlanGuard.Positional("SELECT id FROM clientes WHERE id > $1 AND email = @email AND nome = '@nome' AND cpf = @email -- @x");

        Assert.Equal("SELECT id FROM clientes WHERE id > $1 AND email = $2 AND nome = '@nome' AND cpf = $2 -- @x", sql);
        Assert.Equal(2, count);
        Assert.Equal(("SELECT 1", 0), PgSqlPlanGuard.Positional("SELECT 1"));
    }
}
