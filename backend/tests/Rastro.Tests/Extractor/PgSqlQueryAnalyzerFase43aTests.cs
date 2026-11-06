using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-PGQ-019 em diante: a análise de consulta do PostgreSQL depois do teste numa tela real
// (Fase 43a), numa loja fictícia de vários inquilinos (LojaMultiTenantPg): índice composto, trecho no
// filtro, índice de trigrama, leitura sem limite, OR na mesma coluna, jsonb, !lista.Contains do EF,
// coluna de lista e subconsulta pela chave.
public class PgSqlQueryAnalyzerFase43aTests
{
    private static readonly Lazy<DbSchema> Loja = new(() => DbSchemaExtractor.Extract(LojaMultiTenantPg.Files()));

    private static QueryAnalysis Analyze(string sql) => new PgSqlQueryAnalyzer().Analyze(sql, Loja.Value);

    private static List<QueryFinding> Kinds(string sql, QueryFindingKind kind) => Analyze(sql).Findings.Where(f => f.Kind == kind).ToList();

    private static string Marked(string sql, QueryFinding f) => sql.Substring(f.Start, f.Length);

    private static DbSchema Schema(string ddl) => DbSchemaExtractor.Extract([new SchemaFile("banco/x.sql", ddl)]);

    [Fact] // EXT-PGQ-019
    public void Analyze_IndiceCompostoComAsColunasIniciaisNoFiltro_NaoViraColunaSemIndice()
    {
        // (tenant_id, loja_id) atende o filtro com as duas colunas em igualdade.
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.tenant_id = @t AND i.loja_id = @l LIMIT 10", QueryFindingKind.ColumnWithoutIndex));
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.loja_id = @l AND i.tenant_id = @t LIMIT 10", QueryFindingKind.ColumnWithoutIndex));

        // loja_id sozinha não começa o índice: continua achado, e a mensagem não afirma como fato.
        var sql = "SELECT i.id FROM itens i WHERE i.loja_id = @l LIMIT 10";
        var f = Assert.Single(Kinds(sql, QueryFindingKind.ColumnWithoutIndex));
        Assert.Equal("i.loja_id", Marked(sql, f));
        Assert.Contains("o banco pode ler a tabela inteira", f.Message);
        Assert.Contains("Confira o plano", f.Message);
        Assert.DoesNotContain("o banco lê a tabela inteira", f.Message);

        // Com uma faixa na primeira coluna, o índice é o que o banco usa e loja_id é conferida nas linhas
        // buscadas (coluna residual, Fase 43c).
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.tenant_id > @t AND i.loja_id = @l LIMIT 10", QueryFindingKind.ColumnWithoutIndex));

        // (tenant_id, loja_id, criado_em): a terceira coluna serve a faixa depois de duas igualdades.
        var triple = Schema("CREATE TABLE itens (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, loja_id uuid NOT NULL, criado_em timestamptz NOT NULL);\n"
            + "CREATE INDEX ix_itens_tlc ON itens (tenant_id, loja_id, criado_em);");
        var query = "SELECT i.id FROM itens i WHERE i.tenant_id = @t AND i.loja_id = @l AND i.criado_em > @d LIMIT 10";
        Assert.DoesNotContain(new PgSqlQueryAnalyzer().Analyze(query, triple).Findings, x => x.Kind == QueryFindingKind.ColumnWithoutIndex);
        // Só a segunda coluna e a terceira, sem a primeira: o índice não ajuda.
        var skipped = "SELECT i.id FROM itens i WHERE i.loja_id = @l AND i.criado_em > @d LIMIT 10";
        Assert.Single(new PgSqlQueryAnalyzer().Analyze(skipped, triple).Findings, x => x.Kind == QueryFindingKind.ColumnWithoutIndex);
        // E a junção também: o ON com igualdade conta para o índice composto.
        var join = "SELECT i.id FROM categorias c JOIN itens i ON i.tenant_id = c.tenant_id AND i.loja_id = c.id WHERE c.id = @c LIMIT 10";
        Assert.DoesNotContain(Kinds(join, QueryFindingKind.ColumnWithoutIndex), x => x.Table == "itens");
    }

    [Fact] // EXT-PGQ-020
    public void Analyze_ColunaCitadaNaListaDoSelect_OTrechoApontaOFiltro()
    {
        var sql = "SELECT i.id, i.loja_id\nFROM itens i\nWHERE i.loja_id = @l\nLIMIT 10";

        var f = Assert.Single(Kinds(sql, QueryFindingKind.ColumnWithoutIndex));

        Assert.Equal(("i.loja_id", 3), (Marked(sql, f), f.Line));
        Assert.True(f.Start > sql.IndexOf("WHERE", StringComparison.Ordinal));

        var cast = "SELECT i.id, i.criado_em::date FROM itens i WHERE i.criado_em::date = @d LIMIT 10";
        var function = Assert.Single(Kinds(cast, QueryFindingKind.FunctionOnFilterColumn));
        Assert.True(function.Start > cast.IndexOf("WHERE", StringComparison.Ordinal));

        // No ON da junção também: a coluna aparece antes, na lista do SELECT.
        var join = "SELECT lower(i.status), c.id FROM categorias c JOIN itens i ON lower(i.status) = c.nome WHERE c.id = @c LIMIT 5";
        var onFinding = Assert.Single(Kinds(join, QueryFindingKind.FunctionOnFilterColumn));
        Assert.Equal("lower(i.status)", Marked(join, onFinding));
        Assert.True(onFinding.Start > join.IndexOf("JOIN", StringComparison.Ordinal));
    }

    [Fact] // EXT-PGQ-021
    public void Analyze_IlikeComIndiceDeTrigramaNaColuna_SemAchadoDeCuringaNemDeIndice()
    {
        var sql = "SELECT i.id FROM itens i WHERE i.tenant_id = @t AND i.loja_id = @l AND i.titulo ILIKE '%entrega%' LIMIT 20";

        var findings = Analyze(sql).Findings;

        // Decisão: com GIN ou GiST de trigrama na coluna, o achado de curinga some (o índice atende o ILIKE).
        Assert.DoesNotContain(findings, x => x.Kind is QueryFindingKind.LeadingWildcardLike or QueryFindingKind.ColumnWithoutIndex);
        // Sem o trigrama, o achado continua e continua sugerindo o índice (status só tem hash).
        var status = "SELECT i.id FROM itens i WHERE i.status ILIKE '%aberto%' LIMIT 20";
        var f = Assert.Single(Kinds(status, QueryFindingKind.LeadingWildcardLike));
        Assert.Contains("pg_trgm", f.Message);
        Assert.Contains("o ILIKE pode ler todos os valores", f.Message);
    }

    [Fact] // EXT-PGQ-022
    public void Analyze_OuEntreComparacoesNaMesmaColuna_NaoEAchadoEEntreColunasSugereIn()
    {
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.status = 'a' OR i.status = 'b' LIMIT 10", QueryFindingKind.OrAcrossColumns));
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE (i.status = 'a' OR i.status = 'b' OR i.status = $1) AND i.tenant_id = @t LIMIT 10", QueryFindingKind.OrAcrossColumns));

        var sql = "SELECT i.id FROM itens i WHERE i.status = 'a' OR i.status = 'b' OR i.titulo = 'x' LIMIT 10";
        var f = Assert.Single(Kinds(sql, QueryFindingKind.OrAcrossColumns));
        Assert.Contains("As comparações de i.status com valores cabem num IN", f.Message);
    }

    [Fact] // EXT-PGQ-023
    public void Analyze_OperadorJsonbNaColuna_TextoProprioSemChamarDeConta()
    {
        var sql = "SELECT i.id FROM itens i WHERE i.dados ->> 'cor' = @c AND i.tenant_id = @t LIMIT 10";

        var f = Assert.Single(Kinds(sql, QueryFindingKind.FunctionOnFilterColumn));

        Assert.Equal("i.dados ->> 'cor'", Marked(sql, f));
        Assert.StartsWith("O operador jsonb ->> extrai o valor do documento em cada linha", f.Message);
        Assert.Contains("índice na própria expressão", f.Message);
        Assert.DoesNotContain("A conta", f.Message);
        // A conversão sobre o operador também é o operador jsonb.
        Assert.StartsWith("O operador jsonb ->>", Assert.Single(Kinds("SELECT i.id FROM itens i WHERE (i.dados ->> 'n')::int > 3 LIMIT 5", QueryFindingKind.FunctionOnFilterColumn)).Message);
        // Uma conta de verdade continua sendo conta.
        Assert.StartsWith("A conta", Assert.Single(Kinds("SELECT i.id FROM itens i WHERE i.titulo || 'x' = 'a' LIMIT 5", QueryFindingKind.FunctionOnFilterColumn)).Message);
    }

    [Theory] // EXT-PGQ-024
    [InlineData("SELECT i.id FROM itens i WHERE i.tenant_id = @t AND NOT (i.id = ANY (@ids) AND i.id = ANY (@ids) IS NOT NULL) LIMIT 10")]
    [InlineData("SELECT i.id FROM itens i WHERE i.tenant_id = @t AND i.id <> ALL (@ids) LIMIT 10")]
    [InlineData("SELECT i.id FROM itens i WHERE i.tenant_id = @t AND NOT (i.id = ANY (@ids)) LIMIT 10")]
    [InlineData("SELECT i.id FROM itens i WHERE i.tenant_id = @t AND (i.id <> ALL ($1) OR i.id IS NULL) LIMIT 10")]
    public void Analyze_NegacaoDeListaDoEfCore_NaoViraFuncaoNaColuna(string sql)
    {
        Assert.DoesNotContain(Analyze(sql).Findings, f => f.Kind == QueryFindingKind.FunctionOnFilterColumn);
    }

    [Fact] // EXT-PGQ-025
    public void Analyze_UnnestEAnyEmColunaDeLista_ApontaQueNaoUsaIndiceBtree()
    {
        var unnest = "SELECT i.id FROM itens i WHERE EXISTS (SELECT 1 FROM unnest(i.rotulos) AS x(value) WHERE x.value ILIKE @p)";
        var f = Assert.Single(Kinds(unnest, QueryFindingKind.FunctionOnFilterColumn));
        Assert.Equal(QuerySeverity.Medium, f.Severity);
        Assert.Contains("unnest() lê a lista i.rotulos de cada linha, e um índice B-tree não atende", f.Message);
        Assert.Contains("GIN", f.Message);

        var any = "SELECT i.id FROM itens i WHERE @p = ANY(i.rotulos) AND i.tenant_id = @t LIMIT 10";
        var g = Assert.Single(Kinds(any, QueryFindingKind.FunctionOnFilterColumn));
        Assert.Contains("ANY() lê a lista i.rotulos de cada linha", g.Message);
        // ANY com variável (lista de ids) não é coluna de lista.
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.id = ANY(@ids) LIMIT 10", QueryFindingKind.FunctionOnFilterColumn));
    }

    [Fact] // EXT-PGQ-026
    public void Analyze_SubconsultaLigadaPelaChavePrimaria_GravidadeBaixa()
    {
        var byKey = "SELECT i.id, (SELECT c.nome FROM categorias c WHERE c.id = i.categoria_id) AS categoria FROM itens i WHERE i.tenant_id = @t LIMIT 10";
        var f = Assert.Single(Kinds(byKey, QueryFindingKind.CorrelatedSubquery));
        Assert.Equal(QuerySeverity.Low, f.Severity);
        Assert.Contains("busca categorias pela chave (id)", f.Message);

        var byOther = "SELECT i.id, (SELECT count(*) FROM itens f WHERE f.item_pai_id = i.id) AS filhos FROM itens i WHERE i.tenant_id = @t LIMIT 10";
        Assert.Equal(QuerySeverity.Medium, Assert.Single(Kinds(byOther, QueryFindingKind.CorrelatedSubquery)).Severity);
    }

    [Fact] // EXT-PGQ-027
    public void Analyze_LeituraSemLimitFiltradaSoPorTenant_AchadoBaixo()
    {
        var sql = "SELECT i.id, i.titulo FROM itens i WHERE i.tenant_id = @t";
        var f = Assert.Single(Kinds(sql, QueryFindingKind.ReadWithoutLimit));
        Assert.Equal(QuerySeverity.Low, f.Severity);
        Assert.Equal("i.tenant_id = @t", Marked(sql, f));
        Assert.Contains("filtra itens só por igualdade em itens.tenant_id", f.Message);
        Assert.Contains("não é chave primária nem única", f.Message);

        // Limite, chave primária, filtro que não é só igualdade, agregado e lista filha: sem achado.
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.tenant_id = @t LIMIT 20", QueryFindingKind.ReadWithoutLimit));
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.tenant_id = @t FETCH FIRST 20 ROWS ONLY", QueryFindingKind.ReadWithoutLimit));
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.id = @id", QueryFindingKind.ReadWithoutLimit));
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.tenant_id = @t AND i.criado_em > @d", QueryFindingKind.ReadWithoutLimit));
        Assert.Empty(Kinds("SELECT count(*) FROM itens i WHERE i.tenant_id = @t", QueryFindingKind.ReadWithoutLimit));
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.tenant_id = @t AND i.item_pai_id = @pai", QueryFindingKind.ReadWithoutLimit));
        // Tabela fora do esquema: não dá para saber se a coluna identifica a linha.
        Assert.Empty(Kinds("SELECT e.codigo FROM estoque_central e WHERE e.regiao = @r", QueryFindingKind.ReadWithoutLimit));
        // Só a consulta de fora do SELECT conta.
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.id IN (SELECT f.id FROM itens f WHERE f.tenant_id = @t) LIMIT 5", QueryFindingKind.ReadWithoutLimit));
    }

    [Fact] // EXT-PGQ-030
    public void Analyze_FiltroPelaChaveDeParticao_NaoContaComoListaFilhaEPelaOutraChaveEstrangeiraConta()
    {
        // tenant_id está em todas as tabelas (a chave de partição). tarefas é apontada por quatro tabelas:
        // filtrar por tarefa_id é uma lista filha, e filtrar só pelo tenant não é.
        var schema = Schema("""
            CREATE TABLE tenants (id uuid PRIMARY KEY);
            CREATE TABLE tarefas (id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES tenants (id), titulo text NOT NULL);
            CREATE TABLE subtarefas (id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES tenants (id), tarefa_id uuid NOT NULL REFERENCES tarefas (id), titulo text NOT NULL);
            CREATE TABLE comentarios (id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES tenants (id), tarefa_id uuid NOT NULL REFERENCES tarefas (id));
            CREATE TABLE atividades (id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES tenants (id), tarefa_id uuid NOT NULL REFERENCES tarefas (id));
            CREATE TABLE alertas (id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES tenants (id), tarefa_id uuid NOT NULL REFERENCES tarefas (id));
            CREATE TABLE usuarios (id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES tenants (id));
            CREATE TABLE espacos (id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES tenants (id));
            CREATE TABLE etiquetas (id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES tenants (id));
            CREATE TABLE anexos (id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES tenants (id));
            CREATE INDEX ix_sub_tenant ON subtarefas (tenant_id);
            CREATE INDEX ix_sub_tarefa ON subtarefas (tarefa_id);
            """);
        List<QueryFinding> Found(string sql, QueryFindingKind kind) =>
            new PgSqlQueryAnalyzer().Analyze(sql, schema).Findings.Where(f => f.Kind == kind).ToList();

        Assert.Single(Found("SELECT s.id FROM subtarefas s WHERE s.tenant_id = @t", QueryFindingKind.ReadWithoutLimit));
        Assert.Single(Found("SELECT s.id FROM subtarefas s WHERE s.tenant_id = @t ORDER BY s.titulo", QueryFindingKind.OrderByWithoutLimit));

        // A tarefa é apontada por quatro tabelas e mesmo assim é o pai de uma lista filha.
        Assert.Empty(Found("SELECT s.id FROM subtarefas s WHERE s.tarefa_id = @id", QueryFindingKind.ReadWithoutLimit));
        Assert.Empty(Found("SELECT s.id FROM subtarefas s WHERE s.tarefa_id = @id ORDER BY s.titulo", QueryFindingKind.OrderByWithoutLimit));
        Assert.Empty(Found("SELECT s.id FROM subtarefas s WHERE s.tenant_id = @t AND s.tarefa_id = @id ORDER BY s.titulo", QueryFindingKind.OrderByWithoutLimit));
    }

    [Fact] // EXT-PGQ-028
    public void Analyze_OrderBySemLimitComFiltroPelaChaveEstrangeira_NaoDispara()
    {
        // Subtarefas de uma tarefa: a lista filha de um pai.
        Assert.Empty(Kinds("SELECT i.id FROM itens i WHERE i.item_pai_id = @pai ORDER BY i.criado_em", QueryFindingKind.OrderByWithoutLimit));
        // Só pelo tenant (sem FK), a ordenação inteira continua sendo apontada.
        Assert.Single(Kinds("SELECT i.id FROM itens i WHERE i.tenant_id = @t ORDER BY i.criado_em", QueryFindingKind.OrderByWithoutLimit));
        Assert.Single(Kinds("SELECT i.id FROM itens i ORDER BY i.criado_em", QueryFindingKind.OrderByWithoutLimit));
    }

    [Fact] // EXT-PGQ-029
    public void Analyze_EscalaDeGravidade_ValorMaiorEMaisGraveEAListaComecaPelaAlta()
    {
        Assert.Equal([0, 1, 2], new[] { QuerySeverity.Low, QuerySeverity.Medium, QuerySeverity.High }.Select(s => (int)s));

        var sql = "SELECT * FROM itens i WHERE lower(i.status) = @s ORDER BY i.criado_em";
        var findings = Analyze(sql).Findings;

        Assert.Equal([QuerySeverity.High, QuerySeverity.Medium, QuerySeverity.Low], findings.Select(f => f.Severity).Distinct());
    }
}

// A loja fictícia de vários inquilinos em arquivos, com os caminhos relativos à pasta da fixture.
internal static class LojaMultiTenantPg
{
    public static string Root => Path.Combine(FixtureWorkspace.FixturesRoot, "LojaMultiTenantPg");

    public static List<SchemaFile> Files() =>
        Directory.GetFiles(Root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".sql", StringComparison.Ordinal))
            .Select(f => new SchemaFile(Path.GetRelativePath(Root, f).Replace('\\', '/'), File.ReadAllText(f)))
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
}
