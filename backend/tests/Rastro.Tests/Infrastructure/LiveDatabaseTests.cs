using Rastro.Core.Database;
using Rastro.Extractor.Database;
using Rastro.Infrastructure.Database.Live;

namespace Rastro.Tests.Infrastructure;

// Cenários EXT-DBP-NNN e INF-DBL-NNN: a porta da consulta para o banco vivo (TSqlPlanGuard) e a leitura
// do plano estimado (ShowPlanReader), sem banco nenhum (Fase 40b).
public class LiveDatabaseTests
{
    [Fact] // EXT-DBP-001
    public void Guard_UmaInstrucaoDeLeituraOuEscrita_PassaComAsVariaveis()
    {
        var select = TSqlPlanGuard.Check("SELECT * FROM pedidos WHERE criado_em >= @p0 AND cliente_id = @cliente AND total > @p0");
        Assert.True(select.Allowed);
        Assert.Equal(["@p0", "@cliente"], select.Variables);

        Assert.True(TSqlPlanGuard.Check("UPDATE produtos SET estoque = 0 WHERE id = @id").Allowed);
        Assert.True(TSqlPlanGuard.Check("DELETE FROM log_eventos").Allowed);
        Assert.True(TSqlPlanGuard.Check("WITH c AS (SELECT id FROM clientes) SELECT * FROM c;").Allowed);
    }

    [Theory] // EXT-DBP-002
    [InlineData("SET SHOWPLAN_XML OFF; SELECT 1", "uma instrução só")]
    [InlineData("SELECT 1\nGO\nSELECT 2", "uma instrução só")]
    [InlineData("SET SHOWPLAN_XML OFF", "SET, EXEC, DECLARE e DDL não vão ao banco")]
    [InlineData("EXEC dbo.sp_baixar_estoque 1", "SET, EXEC, DECLARE e DDL não vão ao banco")]
    [InlineData("DROP TABLE pedidos", "SET, EXEC, DECLARE e DDL não vão ao banco")]
    [InlineData("SELECT * INTO copia FROM pedidos", "SELECT INTO cria uma tabela")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=x;', 'SELECT 1') AS r", "OPENROWSET")]
    [InlineData("SELECT * FROM OPENQUERY(outro, 'SELECT 1')", "OPENQUERY")]
    [InlineData("INSERT INTO log_eventos (mensagem) EXEC dbo.sp_x", "EXEC não vai ao banco vivo")]
    [InlineData("DELETE FROM pedidos OUTPUT deleted.id INTO arquivo (id)", "OUTPUT INTO")]
    [InlineData("SELECT FROM WHERE", "não entendeu a consulta")]
    public void Guard_InstrucaoQueNaoPodeIrAoBanco_RecusadaComOMotivo(string sql, string reason)
    {
        var result = TSqlPlanGuard.Check(sql);

        Assert.False(result.Allowed);
        Assert.Contains(reason, result.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    private const string Plan = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4135.4">
          <BatchSequence><Batch><Statements>
            <StmtSimple StatementText="DECLARE @p0 nvarchar(4000);" StatementId="1" StatementType="DECLARE" />
            <StmtSimple StatementText="SELECT ..." StatementId="2" StatementType="SELECT" StatementSubTreeCost="1.0">
              <QueryPlan>
                <Warnings><PlanAffectingConvert ConvertIssue="Seek Plan" Expression="CONVERT_IMPLICIT(nvarchar(4000),[p].[criado_em],0)" /></Warnings>
                <MissingIndexes>
                  <MissingIndexGroup Impact="87.5">
                    <MissingIndex Database="[loja]" Schema="[dbo]" Table="[pedidos]">
                      <ColumnGroup Usage="INEQUALITY"><Column Name="[criado_em]" ColumnId="5" /></ColumnGroup>
                      <ColumnGroup Usage="INCLUDE"><Column Name="[total]" ColumnId="4" /></ColumnGroup>
                    </MissingIndex>
                  </MissingIndexGroup>
                </MissingIndexes>
                <RelOp NodeId="0" PhysicalOp="Sort" LogicalOp="Sort" EstimateRows="10" EstimatedTotalSubtreeCost="1.0">
                  <Sort>
                    <RelOp NodeId="1" PhysicalOp="Hash Match" LogicalOp="Inner Join" EstimateRows="10" EstimatedTotalSubtreeCost="0.8">
                      <Hash>
                        <RelOp NodeId="2" PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan" EstimateRows="100" EstimatedTotalSubtreeCost="0.5">
                          <IndexScan><Object Database="[loja]" Schema="[dbo]" Table="[pedidos]" Index="[PK_pedidos]" Alias="[p]" /></IndexScan>
                        </RelOp>
                        <RelOp NodeId="3" PhysicalOp="Index Seek" LogicalOp="Index Seek" EstimateRows="5" EstimatedTotalSubtreeCost="0.1">
                          <IndexScan><Object Table="[clientes]" Index="[UX_clientes_email]" /></IndexScan>
                        </RelOp>
                      </Hash>
                    </RelOp>
                  </Sort>
                </RelOp>
              </QueryPlan>
            </StmtSimple>
          </Statements></Batch></BatchSequence>
        </ShowPlanXML>
        """;

    [Fact] // INF-DBL-001
    public void ShowPlan_OperacoesPeloCustoProprioVarreduraIndiceFaltandoEAviso()
    {
        var plan = ShowPlanReader.Read(Plan + "\n");

        Assert.Equal(1.0, plan.TotalCost);
        Assert.Equal(["Clustered Index Scan", "Sort", "Hash Match", "Index Seek"], plan.Operations.Select(o => o.PhysicalOp));
        var scan = plan.Operations[0];
        Assert.Equal(("pedidos.PK_pedidos", 50.0, true, 100.0), (scan.Object, scan.Percent, scan.Scan, scan.EstimatedRows));
        Assert.Equal((20.0, false, null), (plan.Operations[1].Percent, plan.Operations[1].Scan, plan.Operations[1].Object));
        Assert.Equal("clientes.UX_clientes_email", plan.Operations[3].Object);

        var missing = Assert.Single(plan.MissingIndexes);
        Assert.Equal(("pedidos", 87.5), (missing.Table, missing.Impact));
        Assert.Empty(missing.Equality);
        Assert.Equal(["criado_em"], missing.Inequality);
        Assert.Equal(["total"], missing.Include);
        Assert.Equal("Conversão implícita que pode impedir a busca no índice: CONVERT_IMPLICIT(nvarchar(4000),[p].[criado_em],0)", Assert.Single(plan.Warnings));
    }

    [Fact] // INF-DBL-002
    public void TipoDoCatalogo_EscritoComoOCodigoEscreve()
    {
        Assert.Equal("nvarchar(30)", SqlServerLiveDatabase.TypeText("nvarchar", 60, 0, 0));
        Assert.Equal("nvarchar(max)", SqlServerLiveDatabase.TypeText("nvarchar", -1, 0, 0));
        Assert.Equal("char(11)", SqlServerLiveDatabase.TypeText("char", 11, 0, 0));
        Assert.Equal("decimal(10,2)", SqlServerLiveDatabase.TypeText("decimal", 9, 10, 2));
        Assert.Equal("datetime2", SqlServerLiveDatabase.TypeText("datetime2", 8, 27, 7));
        Assert.Equal("datetime2(3)", SqlServerLiveDatabase.TypeText("datetime2", 7, 23, 3));
        Assert.Equal("int", SqlServerLiveDatabase.TypeText("int", 4, 10, 0));
    }
}
