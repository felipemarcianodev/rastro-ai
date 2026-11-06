using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-DBQ-016 em diante: as regras da Fase 43a no SQL Server, numa loja fictícia de vários
// inquilinos escrita em .sql: índice composto, trecho no filtro, leitura sem limite, subconsulta pela
// chave e a escala de gravidade.
public class TSqlQueryAnalyzerFase43aTests
{
    private const string Ddl = """
        CREATE TABLE [dbo].[categorias] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL, [nome] nvarchar(80) NOT NULL);
        CREATE TABLE [dbo].[itens] (
            [id] uniqueidentifier NOT NULL,
            [tenant_id] uniqueidentifier NOT NULL,
            [loja_id] uniqueidentifier NOT NULL,
            [categoria_id] uniqueidentifier NULL,
            [item_pai_id] uniqueidentifier NULL,
            [status] nvarchar(30) NOT NULL,
            [criado_em] datetime2 NOT NULL,
            CONSTRAINT [pk_itens] PRIMARY KEY ([id]),
            CONSTRAINT [fk_itens_categorias] FOREIGN KEY ([categoria_id]) REFERENCES [dbo].[categorias] ([id]),
            CONSTRAINT [fk_itens_pai] FOREIGN KEY ([item_pai_id]) REFERENCES [dbo].[itens] ([id])
        );
        CREATE INDEX [ix_itens_tenant_loja] ON [dbo].[itens] ([tenant_id], [loja_id]);
        """;

    private static readonly Lazy<DbSchema> Loja = new(() => DbSchemaExtractor.Extract([new SchemaFile("banco/lojas.sql", Ddl)]));

    private static QueryAnalysis Analyze(string sql) => new TSqlQueryAnalyzer().Analyze(sql, Loja.Value);

    private static List<QueryFinding> Kinds(string sql, QueryFindingKind kind) => Analyze(sql).Findings.Where(f => f.Kind == kind).ToList();

    private static string Marked(string sql, QueryFinding f) => sql.Substring(f.Start, f.Length);

    [Fact] // EXT-DBQ-016
    public void Analyze_IndiceCompostoComAsColunasIniciaisNoFiltro_NaoViraColunaSemIndice()
    {
        Assert.Empty(Kinds("SELECT TOP 10 i.id FROM itens i WHERE i.tenant_id = @t AND i.loja_id = @l", QueryFindingKind.ColumnWithoutIndex));
        Assert.Empty(Kinds("SELECT TOP 10 i.id FROM itens i WHERE i.loja_id = @l AND i.tenant_id = @t", QueryFindingKind.ColumnWithoutIndex));

        var sql = "SELECT TOP 10 i.id, i.loja_id FROM itens i WHERE i.loja_id = @l";
        var f = Assert.Single(Kinds(sql, QueryFindingKind.ColumnWithoutIndex));
        // O trecho aponta a coluna do filtro, e não a da lista do SELECT.
        Assert.Equal("i.loja_id", Marked(sql, f));
        Assert.True(f.Start > sql.IndexOf("WHERE", StringComparison.Ordinal));
        Assert.Contains("o banco pode ler a tabela inteira", f.Message);
        Assert.DoesNotContain("o banco lê a tabela inteira", f.Message);
    }

    [Fact] // EXT-DBQ-017
    public void Analyze_LeituraSemTopFiltradaSoPorTenant_AchadoBaixoEListaFilhaNao()
    {
        var sql = "SELECT i.id FROM itens i WHERE i.tenant_id = @t";
        var f = Assert.Single(Kinds(sql, QueryFindingKind.ReadWithoutLimit));
        Assert.Equal(QuerySeverity.Low, f.Severity);
        Assert.Equal("i.tenant_id = @t", Marked(sql, f));
        Assert.Contains("não tem TOP nem OFFSET/FETCH", f.Message);

        Assert.Empty(Kinds("SELECT TOP 20 i.id FROM itens i WHERE i.tenant_id = @t", QueryFindingKind.ReadWithoutLimit));
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.id = @id", QueryFindingKind.ReadWithoutLimit));
        Assert.Empty(Kinds("SELECT COUNT(*) FROM itens i WHERE i.tenant_id = @t", QueryFindingKind.ReadWithoutLimit));
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.tenant_id = @t AND i.criado_em > @d", QueryFindingKind.ReadWithoutLimit));
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.item_pai_id = @pai ORDER BY i.criado_em", QueryFindingKind.ReadWithoutLimit));
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.item_pai_id = @pai ORDER BY i.criado_em", QueryFindingKind.OrderByWithoutLimit));
        Assert.Single(Kinds("SELECT i.id FROM itens i WHERE i.tenant_id = @t ORDER BY i.criado_em", QueryFindingKind.OrderByWithoutLimit));
    }

    [Fact] // EXT-DBQ-018
    public void Analyze_SubconsultaLigadaPelaChavePrimaria_GravidadeBaixa()
    {
        var byKey = "SELECT TOP 10 i.id, (SELECT c.nome FROM categorias c WHERE c.id = i.categoria_id) AS categoria FROM itens i WHERE i.tenant_id = @t";
        var f = Assert.Single(Kinds(byKey, QueryFindingKind.CorrelatedSubquery));
        Assert.Equal(QuerySeverity.Low, f.Severity);
        Assert.Contains("busca categorias pela chave (id)", f.Message);

        var byOther = "SELECT TOP 10 i.id, (SELECT COUNT(*) FROM itens f WHERE f.item_pai_id = i.id) AS filhos FROM itens i WHERE i.tenant_id = @t";
        Assert.Equal(QuerySeverity.Medium, Assert.Single(Kinds(byOther, QueryFindingKind.CorrelatedSubquery)).Severity);
    }

    [Fact] // EXT-DBQ-019
    public void Analyze_EscalaDeGravidade_ValorMaiorEMaisGraveEAListaComecaPelaAlta()
    {
        var sql = "SELECT * FROM itens i WHERE YEAR(i.criado_em) = 2026 ORDER BY i.criado_em";

        var findings = Analyze(sql).Findings;

        Assert.Equal([QuerySeverity.High, QuerySeverity.Medium, QuerySeverity.Low], findings.Select(f => f.Severity));
        Assert.Equal([2, 1, 0], findings.Select(f => (int)f.Severity));
    }
}
