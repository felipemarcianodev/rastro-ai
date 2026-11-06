using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-DBQ-020 em diante: o reteste da Fase 43a num sistema real (Fase 43c), no SQL Server.
public class TSqlQueryAnalyzerFase43cTests
{
    private static readonly DbSchema Sistema = DbSchemaExtractor.Extract([new SchemaFile("banco/x.sql", """
        CREATE TABLE [dbo].[tarefas] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL, [space_id] uniqueidentifier NOT NULL, [archived_at] datetime2 NULL, [titulo] nvarchar(80) NOT NULL);
        CREATE TABLE [dbo].[subtarefas] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL, [tarefa_id] uniqueidentifier NOT NULL, [titulo] nvarchar(80) NOT NULL,
            CONSTRAINT [fk_sub_tarefa] FOREIGN KEY ([tarefa_id]) REFERENCES [dbo].[tarefas] ([id]));
        CREATE TABLE [dbo].[comentarios] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL, [tarefa_id] uniqueidentifier NOT NULL,
            CONSTRAINT [fk_com_tarefa] FOREIGN KEY ([tarefa_id]) REFERENCES [dbo].[tarefas] ([id]));
        CREATE TABLE [dbo].[atividades] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL, [tarefa_id] uniqueidentifier NOT NULL,
            CONSTRAINT [fk_ati_tarefa] FOREIGN KEY ([tarefa_id]) REFERENCES [dbo].[tarefas] ([id]));
        CREATE TABLE [dbo].[alertas] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL, [tarefa_id] uniqueidentifier NOT NULL, [sent] bit NOT NULL, [alert_date_time] datetime2 NOT NULL,
            CONSTRAINT [fk_ale_tarefa] FOREIGN KEY ([tarefa_id]) REFERENCES [dbo].[tarefas] ([id]));
        CREATE TABLE [dbo].[usuarios] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL);
        CREATE TABLE [dbo].[espacos] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL);
        CREATE TABLE [dbo].[etiquetas] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL);
        CREATE TABLE [dbo].[anexos] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL);
        CREATE TABLE [dbo].[convites] ([id] uniqueidentifier NOT NULL PRIMARY KEY, [tenant_id] uniqueidentifier NOT NULL);
        CREATE INDEX [ix_tarefas_tenant_space] ON [dbo].[tarefas] ([tenant_id], [space_id]);
        CREATE INDEX [ix_alertas_sent_data] ON [dbo].[alertas] ([sent], [alert_date_time]);
        CREATE INDEX [ix_sub_tenant] ON [dbo].[subtarefas] ([tenant_id]);
        """)]);

    private static List<QueryFinding> Kinds(string sql, QueryFindingKind kind) =>
        new TSqlQueryAnalyzer().Analyze(sql, Sistema).Findings.Where(f => f.Kind == kind).ToList();

    [Fact] // EXT-DBQ-020
    public void Analyze_ColunaResidualDepoisDeIndiceUsado_NaoViraColunaSemIndice()
    {
        Assert.Empty(Kinds("SELECT TOP 10 t.id FROM tarefas t WHERE t.tenant_id = @t AND t.space_id = @s AND t.archived_at IS NULL", QueryFindingKind.ColumnWithoutIndex));
        Assert.Empty(Kinds("SELECT TOP 10 t.id FROM tarefas t WHERE t.tenant_id > @t AND t.titulo = @x", QueryFindingKind.ColumnWithoutIndex));
        // Nenhum índice atende nenhuma coluna do filtro: os dois são achados.
        Assert.Equal(2, Kinds("SELECT TOP 10 t.id FROM tarefas t WHERE t.titulo = @x AND t.archived_at = @d", QueryFindingKind.ColumnWithoutIndex).Count);
        // A junção, por tabela: o residual do ON fica sem achado quando o índice da tabela atende o ON.
        Assert.Empty(Kinds("SELECT TOP 10 s.id FROM tarefas t JOIN subtarefas s ON s.tenant_id = t.tenant_id AND s.titulo = t.titulo WHERE t.id = @i", QueryFindingKind.ColumnWithoutIndex));
    }

    [Fact] // EXT-DBQ-021
    public void Analyze_BitComoIgualdade_ContaNoIndiceComposto()
    {
        Assert.Empty(Kinds("SELECT TOP 10 a.id FROM alertas a WHERE a.sent = 0 AND a.alert_date_time <= GETDATE()", QueryFindingKind.ColumnWithoutIndex));
        Assert.Empty(Kinds("SELECT TOP 10 a.id FROM alertas a WHERE a.alert_date_time <= GETDATE() AND a.sent = 1", QueryFindingKind.ColumnWithoutIndex));
    }

    [Fact] // EXT-DBQ-022
    public void Analyze_ListaFilhaPelaChaveEstrangeiraQueNaoEDeParticao_NaoDisparaELeituraPeloTenantDispara()
    {
        // tenant_id está em todas as dez tabelas (a de partição); tarefa_id, em quatro, é só o pai de uma lista filha.
        Assert.Single(Kinds("SELECT s.id FROM subtarefas s WHERE s.tenant_id = @t", QueryFindingKind.ReadWithoutLimit));
        Assert.Single(Kinds("SELECT s.id FROM subtarefas s WHERE s.tenant_id = @t ORDER BY s.titulo", QueryFindingKind.OrderByWithoutLimit));
        Assert.Empty(Kinds("SELECT s.id FROM subtarefas s WHERE s.tarefa_id = @id", QueryFindingKind.ReadWithoutLimit));
        Assert.Empty(Kinds("SELECT s.id FROM subtarefas s WHERE s.tarefa_id = @id ORDER BY s.titulo", QueryFindingKind.OrderByWithoutLimit));
    }
}
