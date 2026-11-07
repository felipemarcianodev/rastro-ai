using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-DBQ-025 em diante: a leitura sem limite pelo tamanho da tabela (Fase 44) e o IS [NOT] NULL
// que não limita a leitura, no SQL Server.
public class TSqlQueryAnalyzerFase44Tests
{
    private static readonly DbSchema Base = DbSchemaExtractor.Extract([new SchemaFile("banco/x.sql", """
        CREATE TABLE [dbo].[tarefas] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL, [space_id] uniqueidentifier NOT NULL, [archived_at] datetime2 NULL, [titulo] nvarchar(80) NOT NULL);
        CREATE TABLE [dbo].[spaces] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL);
        CREATE TABLE [dbo].[etiquetas] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL);
        """)]);

    private static DbSchema Sized(long? tarefas) => Base with
    {
        Tables = Base.Tables.Select(t => t.Name == "tarefas" ? t with { EstimatedRows = tarefas } : t).ToList()
    };

    private static List<QueryFinding> Reads(string sql, DbSchema schema) =>
        new TSqlQueryAnalyzer().Analyze(sql, schema).Findings.Where(f => f.Kind == QueryFindingKind.ReadWithoutLimit).ToList();

    private const string PeloEspaco = "SELECT t.id FROM tarefas t WHERE t.tenant_id = @t AND t.space_id = @s AND t.archived_at IS NULL";

    [Fact] // EXT-DBQ-025
    public void Analyze_ListaPeloEspacoEmTabelaGrande_DisparaComONumeroEstimado()
    {
        var found = Assert.Single(Reads(PeloEspaco, Sized(2_500_000)));

        Assert.Contains("2.500.000", found.Message);
        Assert.Contains("estimativa do banco vivo", found.Message);
    }

    [Fact] // EXT-DBQ-026
    public void Analyze_ListaPeloEspacoSemEstimativaOuEmTabelaMedia_ContinuaSilenciada()
    {
        Assert.Empty(Reads(PeloEspaco, Sized(null)));
        Assert.Empty(Reads(PeloEspaco, Sized(TableSize.LargeRows - 1)));
        Assert.Single(Reads(PeloEspaco, Sized(TableSize.LargeRows)));
    }

    [Fact] // EXT-DBQ-027
    public void Analyze_LeituraPeloTenantEmTabelaPequena_NaoEAchado()
    {
        const string sql = "SELECT t.id FROM tarefas t WHERE t.tenant_id = @t";

        Assert.Empty(Reads(sql, Sized(TableSize.SmallRows - 1)));
        Assert.Single(Reads(sql, Sized(TableSize.SmallRows)));
        var semEstimativa = Assert.Single(Reads(sql, Sized(null)));
        Assert.DoesNotContain("estimativa", semEstimativa.Message);
    }

    [Fact] // EXT-DBQ-028
    public void Analyze_IsNotNullJuntoDaIgualdadeDoTenant_NaoImpedeOAchado()
    {
        Assert.Single(Reads("SELECT t.id FROM tarefas t WHERE t.tenant_id = @t AND t.archived_at IS NOT NULL", Sized(null)));
        Assert.Single(Reads("SELECT t.id FROM tarefas t WHERE t.archived_at IS NOT NULL AND t.tenant_id = @t", Sized(null)));
        Assert.Single(Reads("SELECT t.id FROM tarefas t WHERE t.tenant_id = @t AND t.archived_at IS NULL", Sized(null)));
        Assert.Empty(Reads("SELECT t.id FROM tarefas t WHERE t.archived_at IS NOT NULL", Sized(null)));
        Assert.Empty(Reads("SELECT t.id FROM tarefas t WHERE t.id = @i AND t.archived_at IS NOT NULL", Sized(null)));
    }

    private static readonly DbSchema Hierarquia = DbSchemaExtractor.Extract([new SchemaFile("banco/h.sql", """
        CREATE TABLE [dbo].[espacos] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL);
        CREATE TABLE [dbo].[tarefas] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL, [espaco_id] uniqueidentifier NOT NULL, [tipo_id] uniqueidentifier NOT NULL, [arquivada_em] datetime2 NULL);
        CREATE TABLE [dbo].[subtarefas] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL, [tarefa_id] uniqueidentifier NOT NULL);
        CREATE TABLE [dbo].[comentarios] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL, [tarefa_id] uniqueidentifier NOT NULL);
        CREATE TABLE [dbo].[atividades] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL, [tarefa_id] uniqueidentifier NOT NULL);
        CREATE TABLE [dbo].[status_espaco] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL, [espaco_id] uniqueidentifier NOT NULL);
        CREATE TABLE [dbo].[etiquetas2] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL);
        CREATE TABLE [dbo].[anexos] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL);
        CREATE TABLE [dbo].[filtros] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL);
        CREATE TABLE [dbo].[papeis] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL);
        """)]);

    private static List<QueryFinding> Hier(string sql) => Reads(sql, Hierarquia);

    [Fact] // EXT-DBQ-029
    public void Analyze_ListaDoConteinerEmEntidadePrincipal_DisparaComOMotivo()
    {
        var found = Assert.Single(Hier("SELECT t.* FROM tarefas t WHERE t.tenant_id = @t AND t.espaco_id = @e AND t.arquivada_em IS NULL"));

        Assert.Contains("tarefas é apontada por 3 tabelas", found.Message);
        Assert.Contains("espaco_id", found.Message);
        Assert.Single(Hier("SELECT t.* FROM tarefas t WHERE t.tenant_id = @t AND t.tipo_id = @i AND t.arquivada_em IS NULL"));
    }

    [Fact] // EXT-DBQ-030
    public void Analyze_ListaFilhaDeFolhaOuDeTabelaApontadaPorUma_ContinuaSilenciada()
    {
        Assert.Empty(Hier("SELECT s.id FROM subtarefas s WHERE s.tenant_id = @t AND s.tarefa_id = @a"));
        Assert.Empty(Hier("SELECT s.id FROM status_espaco s WHERE s.tenant_id = @t AND s.espaco_id = @e"));
    }

    [Fact] // EXT-DBQ-031
    public void Analyze_OrderByDaListaDoConteinerEmEntidadePrincipal_Dispara()
    {
        var all = new TSqlQueryAnalyzer().Analyze("SELECT t.id FROM tarefas t WHERE t.tenant_id = @t AND t.espaco_id = @e ORDER BY t.id", Hierarquia).Findings;
        Assert.Contains(all, f => f.Kind == QueryFindingKind.OrderByWithoutLimit);
        var folha = new TSqlQueryAnalyzer().Analyze("SELECT s.id FROM subtarefas s WHERE s.tenant_id = @t AND s.tarefa_id = @a ORDER BY s.id", Hierarquia).Findings;
        Assert.DoesNotContain(folha, f => f.Kind == QueryFindingKind.OrderByWithoutLimit);
    }

    private static readonly DbSchema Assinaturas = DbSchemaExtractor.Extract([new SchemaFile("banco/a.sql", """
        CREATE TABLE [dbo].[assinaturas] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL, [email_teste] nvarchar(200) NULL, [titulo] nvarchar(200) NULL, [detalhes] nvarchar(200) NULL);
        CREATE INDEX ix_assinaturas_email ON [dbo].[assinaturas] ([email_teste]);
        """)]);

    private static List<QueryFinding> Funcoes(string sql) =>
        new TSqlQueryAnalyzer().Analyze(sql, Assinaturas).Findings.Where(f => f.Kind == QueryFindingKind.FunctionOnFilterColumn).ToList();

    [Fact] // EXT-DBQ-032
    public void Analyze_FuncaoNaColunaDentroDeExistsNaListaDoSelect_Dispara()
    {
        Assert.Single(Funcoes("SELECT CASE WHEN EXISTS (SELECT 1 FROM assinaturas AS s WHERE s.tenant_id = @t AND s.email_teste IS NOT NULL AND LOWER(s.email_teste) = @e) THEN 1 ELSE 0 END"));
        Assert.Single(Funcoes("SELECT CASE WHEN EXISTS (SELECT 1 FROM assinaturas AS a WHERE a.tenant_id = @t AND ((a.detalhes IS NOT NULL AND a.detalhes LIKE @m) OR LOWER(a.titulo) = @e)) THEN 1 ELSE 0 END"));
    }

    [Fact] // EXT-DBQ-033
    public void Analyze_SelectEstrelaDentroDeExistsNaListaDoSelect_NaoEAchado()
    {
        var all = new TSqlQueryAnalyzer().Analyze("SELECT CASE WHEN EXISTS (SELECT * FROM assinaturas AS s WHERE s.tenant_id = @t) THEN 1 ELSE 0 END", Assinaturas).Findings;

        Assert.DoesNotContain(all, f => f.Kind == QueryFindingKind.SelectStar);
    }
}
