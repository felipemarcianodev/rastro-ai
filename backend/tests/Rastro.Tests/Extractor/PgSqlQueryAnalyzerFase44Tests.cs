using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-PGQ-043 em diante: a leitura sem limite pelo tamanho da tabela (Fase 44) e o IS [NOT] NULL
// que não limita a leitura. O tamanho vem do catálogo do banco vivo (DbTable.EstimatedRows).
public class PgSqlQueryAnalyzerFase44Tests
{
    private static readonly DbSchema Base = DbSchemaExtractor.Extract([new SchemaFile("banco/x.sql", """
        CREATE TABLE task_items (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, space_id uuid NOT NULL, archived_at timestamptz NULL, titulo text NOT NULL);
        CREATE TABLE spaces (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, nome text NOT NULL);
        CREATE TABLE labels (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, nome text NOT NULL);
        """)]);

    private static DbSchema Sized(long? taskItems, long? spaces = null) => Base with
    {
        Tables = Base.Tables.Select(t => t.Name switch
        {
            "task_items" => t with { EstimatedRows = taskItems },
            "spaces" => t with { EstimatedRows = spaces },
            _ => t
        }).ToList()
    };

    private static List<QueryFinding> Reads(string sql, DbSchema schema) =>
        new PgSqlQueryAnalyzer().Analyze(sql, schema).Findings.Where(f => f.Kind == QueryFindingKind.ReadWithoutLimit).ToList();

    private const string PeloEspaco = "SELECT t.id FROM task_items t WHERE t.tenant_id = @t AND t.space_id = @s AND t.archived_at IS NULL";

    [Fact] // EXT-PGQ-043
    public void Analyze_ListaPeloEspacoEmTabelaGrande_DisparaComONumeroEstimado()
    {
        var found = Assert.Single(Reads(PeloEspaco, Sized(2_500_000)));

        Assert.Contains("2.500.000", found.Message);
        Assert.Contains("estimativa do banco vivo", found.Message);
    }

    [Fact] // EXT-PGQ-044
    public void Analyze_ListaPeloEspacoSemEstimativaOuEmTabelaMedia_ContinuaSilenciada()
    {
        // Sem banco vivo (sem estimativa) e abaixo do limite de tabela grande, a exceção da lista filha vale.
        Assert.Empty(Reads(PeloEspaco, Sized(null)));
        Assert.Empty(Reads(PeloEspaco, Sized(TableSize.LargeRows - 1)));
        Assert.Single(Reads(PeloEspaco, Sized(TableSize.LargeRows)));
    }

    [Fact] // EXT-PGQ-045
    public void Analyze_LeituraPeloTenantEmTabelaPequena_NaoEAchado()
    {
        const string sql = "SELECT t.id FROM task_items t WHERE t.tenant_id = @t";

        Assert.Empty(Reads(sql, Sized(TableSize.SmallRows - 1)));
        // No limite e sem estimativa, o achado de hoje continua.
        Assert.Single(Reads(sql, Sized(TableSize.SmallRows)));
        var semEstimativa = Assert.Single(Reads(sql, Sized(null)));
        Assert.DoesNotContain("estimativa", semEstimativa.Message);
    }

    [Fact] // EXT-PGQ-046
    public void Analyze_IsNotNullJuntoDaIgualdadeDoTenant_NaoImpedeOAchado()
    {
        // Q04 do reteste: archived_at IS NOT NULL não limita a leitura.
        Assert.Single(Reads("SELECT t.id FROM task_items t WHERE t.tenant_id = @t AND t.archived_at IS NOT NULL", Sized(null)));
        Assert.Single(Reads("SELECT t.id FROM task_items t WHERE t.archived_at IS NOT NULL AND t.tenant_id = @t", Sized(null)));
        // IS NULL também é ignorado: sozinho, não há filtro que limite nem que identifique.
        Assert.Single(Reads("SELECT t.id FROM task_items t WHERE t.tenant_id = @t AND t.archived_at IS NULL", Sized(null)));
        Assert.Empty(Reads("SELECT t.id FROM task_items t WHERE t.archived_at IS NOT NULL", Sized(null)));
        // Uma chave primária junto continua identificando a linha.
        Assert.Empty(Reads("SELECT t.id FROM task_items t WHERE t.id = @i AND t.archived_at IS NOT NULL", Sized(null)));
    }

    // Esquema com uma entidade principal (tarefas, apontada por três tabelas), uma folha (subtarefas), uma tabela
    // apontada por uma só (status_espaco) e o pai (espacos).
    private static readonly DbSchema Hierarquia = DbSchemaExtractor.Extract([new SchemaFile("banco/h.sql", """
        CREATE TABLE espacos (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, nome text NOT NULL);
        CREATE TABLE tarefas (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, espaco_id uuid NOT NULL, tipo_id uuid NOT NULL, arquivada_em timestamptz NULL);
        CREATE TABLE subtarefas (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, tarefa_id uuid NOT NULL);
        CREATE TABLE comentarios (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, tarefa_id uuid NOT NULL);
        CREATE TABLE atividades (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, tarefa_id uuid NOT NULL);
        CREATE TABLE status_espaco (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, espaco_id uuid NOT NULL, nome text NOT NULL);
        CREATE TABLE etiquetas (id uuid PRIMARY KEY, tenant_id uuid NOT NULL);
        CREATE TABLE anexos (id uuid PRIMARY KEY, tenant_id uuid NOT NULL);
        CREATE TABLE filtros (id uuid PRIMARY KEY, tenant_id uuid NOT NULL);
        CREATE TABLE papeis (id uuid PRIMARY KEY, tenant_id uuid NOT NULL);
        """)]);

    private static List<QueryFinding> Hier(string sql) => Reads(sql, Hierarquia);

    [Fact] // EXT-PGQ-047
    public void Analyze_ListaDoConteinerEmEntidadePrincipal_DisparaComOMotivo()
    {
        var found = Assert.Single(Hier("SELECT t.* FROM tarefas t WHERE t.tenant_id = @t AND t.espaco_id = @e AND t.arquivada_em IS NULL"));

        Assert.Contains("tarefas é apontada por 3 tabelas", found.Message);
        Assert.Contains("espaco_id", found.Message);
        // Sem a tabela tipos, tipo_id nunca foi chave estrangeira: o achado já existia.
        Assert.Single(Hier("SELECT t.* FROM tarefas t WHERE t.tenant_id = @t AND t.tipo_id = @i AND t.arquivada_em IS NULL"));
    }

    [Fact] // EXT-PGQ-048
    public void Analyze_ListaFilhaDeFolhaOuDeTabelaApontadaPorUma_ContinuaSilenciada()
    {
        Assert.Empty(Hier("SELECT s.id FROM subtarefas s WHERE s.tenant_id = @t AND s.tarefa_id = @a"));
        Assert.Empty(Hier("SELECT s.id FROM status_espaco s WHERE s.tenant_id = @t AND s.espaco_id = @e"));
    }

    [Fact] // EXT-PGQ-049
    public void Analyze_OrderByDaListaDoConteinerEmEntidadePrincipal_Dispara()
    {
        var all = new PgSqlQueryAnalyzer().Analyze("SELECT t.id FROM tarefas t WHERE t.tenant_id = @t AND t.espaco_id = @e ORDER BY t.id", Hierarquia).Findings;
        Assert.Contains(all, f => f.Kind == QueryFindingKind.OrderByWithoutLimit);
        var folha = new PgSqlQueryAnalyzer().Analyze("SELECT s.id FROM subtarefas s WHERE s.tenant_id = @t AND s.tarefa_id = @a ORDER BY s.id", Hierarquia).Findings;
        Assert.DoesNotContain(folha, f => f.Kind == QueryFindingKind.OrderByWithoutLimit);
    }

    private static readonly DbSchema Assinaturas = DbSchemaExtractor.Extract([new SchemaFile("banco/a.sql", """
        CREATE TABLE assinaturas (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, email_teste text NULL, titulo text NULL, detalhes text NULL);
        CREATE INDEX ix_assinaturas_email ON assinaturas (email_teste);
        """)]);

    private static List<QueryFinding> Funcoes(string sql) =>
        new PgSqlQueryAnalyzer().Analyze(sql, Assinaturas).Findings.Where(f => f.Kind == QueryFindingKind.FunctionOnFilterColumn).ToList();

    [Fact] // EXT-PGQ-050
    public void Analyze_FuncaoNaColunaDentroDeExistsNaListaDoSelect_Dispara()
    {
        // Antes o EXISTS na lista do SELECT não era analisado: nenhum achado e nenhuma tabela.
        var found = Assert.Single(Funcoes("SELECT EXISTS (SELECT 1 FROM assinaturas AS s WHERE s.tenant_id = @t AND s.email_teste IS NOT NULL AND lower(s.email_teste) = @e)"));

        Assert.Contains("s.email_teste", found.Message);
        Assert.Single(Funcoes("SELECT EXISTS (SELECT 1 FROM assinaturas AS a WHERE a.tenant_id = @t AND ((a.detalhes IS NOT NULL AND a.detalhes LIKE @m) OR lower(a.titulo) = @e))"));
        var tabelas = new PgSqlQueryAnalyzer().Analyze("SELECT EXISTS (SELECT 1 FROM assinaturas AS s WHERE s.tenant_id = @t)", Assinaturas).Tables;
        Assert.Contains(tabelas, t => t.Name == "assinaturas");
    }

    [Fact] // EXT-PGQ-051
    public void Analyze_FuncaoNaColunaNoExistsDoWhere_ContinuaDisparando()
    {
        Assert.Single(Funcoes("SELECT 1 FROM assinaturas o WHERE EXISTS (SELECT 1 FROM assinaturas s WHERE s.tenant_id = o.tenant_id AND lower(s.email_teste) = @e)"));
    }

    private static readonly DbSchema Busca = DbSchemaExtractor.Extract([new SchemaFile("banco/b.sql", """
        CREATE TABLE itens (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, titulo text NOT NULL, nota text NOT NULL);
        CREATE INDEX ix_itens_tenant ON itens (tenant_id);
        CREATE INDEX ix_itens_titulo_trgm ON itens USING gin (titulo gin_trgm_ops);
        """)]);

    private static List<QueryFinding> Like(string sql) =>
        new PgSqlQueryAnalyzer().Analyze(sql, Busca).Findings.Where(f => f.Kind == QueryFindingKind.LeadingWildcardLike).ToList();

    [Fact] // EXT-PGQ-052
    public void Analyze_IlikeComCuringaNoComecoEEscapeVazio_DisparaSoSemIndiceTrigrama()
    {
        // Com o índice trigrama na coluna o ILIKE não é achado; sem ele, é, com o ESCAPE vazio que o EF Core escreve.
        Assert.Empty(Like("SELECT i.id FROM itens i WHERE i.tenant_id = @t AND i.titulo ILIKE '%x%' ESCAPE '' LIMIT 5"));
        Assert.Single(Like("SELECT i.id FROM itens i WHERE i.tenant_id = @t AND i.nota ILIKE '%x%' ESCAPE '' LIMIT 5"));
        Assert.Single(Like("SELECT i.id FROM itens i WHERE i.tenant_id = @t AND i.nota ILIKE '%x%' LIMIT 5"));
    }
}
