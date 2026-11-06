using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-PGQ-NNN: análise de consulta sem IA no dialeto do PostgreSQL (Fase 42), uma consulta da
// loja fictícia LojaBancoPg por achado (as regras da Fase 40, mais o ILIKE e a conversão com ::), com o
// trecho marcado no texto, mais a consulta limpa, o erro de leitura e a tabela fora do esquema.
public class PgSqlQueryAnalyzerTests
{
    private static readonly Lazy<DbSchema> Loja = new(() => DbSchemaExtractor.Extract(LojaBancoPg.Files()));

    private static QueryAnalysis Analyze(string sql) => new PgSqlQueryAnalyzer().Analyze(sql, Loja.Value);

    private static QueryFinding One(string sql, QueryFindingKind kind) => Assert.Single(Analyze(sql).Findings, f => f.Kind == kind);

    private static string Marked(string sql, QueryFinding f) => sql.Substring(f.Start, f.Length);

    [Fact] // EXT-PGQ-001
    public void Analyze_ConsultaLimpa_SemAchadoComParametrosDoNpgsqlEDoPostgreSql()
    {
        var sql = """
            SELECT p.id, p.total, c.nome
            FROM pedidos p
            JOIN clientes c ON c.id = p.cliente_id
            WHERE p.cliente_id = @cliente AND p.id > $1
            ORDER BY p.id DESC
            LIMIT 20
            """;

        var analysis = Analyze(sql);

        Assert.Null(analysis.Error);
        Assert.Equal(SqlDialect.PostgreSql, analysis.Dialect);
        Assert.Equal(1, analysis.Statements);
        Assert.Empty(analysis.Findings);
        Assert.Equal([new QueryTableUse("pedidos", "p", true), new QueryTableUse("clientes", "c", true)], analysis.Tables);
        // FETCH FIRST também limita.
        Assert.Empty(Analyze("SELECT id FROM pedidos ORDER BY id FETCH FIRST 10 ROWS ONLY").Findings);
    }

    [Fact] // EXT-PGQ-002
    public void Analyze_SelectEstrela_CitaAsColunasEODadoPessoal()
    {
        var sql = "SELECT * FROM clientes WHERE id = 1";

        var f = One(sql, QueryFindingKind.SelectStar);

        Assert.Equal("*", Marked(sql, f));
        Assert.Contains("todas as 7 colunas de clientes, inclusive clientes.email e clientes.cpf, marcadas como dado pessoal", f.Message);
        // O * do count(*) não é SELECT *.
        Assert.DoesNotContain(Analyze("SELECT count(*) FROM clientes").Findings, x => x.Kind == QueryFindingKind.SelectStar);
    }

    [Fact] // EXT-PGQ-003
    public void Analyze_ConversaoComDoisPontosNaColunaDoFiltro_AltaComOTipo()
    {
        var sql = "SELECT p.id FROM pedidos p WHERE p.criado_em::date = $1 LIMIT 10";

        var f = One(sql, QueryFindingKind.FunctionOnFilterColumn);

        Assert.Equal(QuerySeverity.High, f.Severity);
        Assert.Equal("p.criado_em::date", Marked(sql, f));
        Assert.Contains("A conversão ::date roda em cada linha e impede o uso de índice em p.criado_em", f.Message);
        Assert.Contains("converta o valor, não a coluna", f.Message);
        Assert.Contains("E pedidos.criado_em nem tem índice.", f.Message);
        Assert.DoesNotContain(Analyze(sql).Findings, x => x.Kind == QueryFindingKind.ColumnWithoutIndex);
    }

    [Fact] // EXT-PGQ-004
    public void Analyze_FuncaoNaColunaDoFiltro_ExtractELower()
    {
        var sql = "SELECT p.id FROM pedidos p WHERE extract(year from p.criado_em) = 2026 LIMIT 10";

        Assert.Equal("extract(year from p.criado_em)", Marked(sql, One(sql, QueryFindingKind.FunctionOnFilterColumn)));

        var lower = "SELECT id FROM clientes WHERE lower(email) = @email LIMIT 1";
        var f = One(lower, QueryFindingKind.FunctionOnFilterColumn);
        Assert.Equal("lower(email)", Marked(lower, f));
        Assert.StartsWith("LOWER() roda em cada linha", f.Message);
    }

    [Fact] // EXT-PGQ-005
    public void Analyze_IlikeComCuringaNoComeco_DizQueOIndiceNaoAjudaESugereTrigrama()
    {
        var sql = "SELECT c.id FROM clientes c WHERE c.email ILIKE '%@gmail.com' LIMIT 10";

        var f = One(sql, QueryFindingKind.LeadingWildcardLike);

        Assert.Equal("c.email ILIKE '%@gmail.com'", Marked(sql, f));
        Assert.Contains("o ILIKE pode ler todos os valores de c.email: o índice ux_clientes_email existe, mas não ajuda", f.Message);
        Assert.Contains("pg_trgm", f.Message);
        // O curinga concatenado com || também conta; sem curinga no começo, não.
        Assert.Single(Analyze("SELECT id FROM clientes WHERE email LIKE '%' || $1 LIMIT 5").Findings, x => x.Kind == QueryFindingKind.LeadingWildcardLike);
        Assert.DoesNotContain(Analyze("SELECT id FROM clientes WHERE email ILIKE 'ana%' LIMIT 5").Findings, x => x.Kind == QueryFindingKind.LeadingWildcardLike);
    }

    [Fact] // EXT-PGQ-006
    public void Analyze_OrEntreColunasDiferentes_ViraAchado()
    {
        var sql = "SELECT id FROM clientes WHERE nome = @nome OR email = @email LIMIT 10";

        var f = One(sql, QueryFindingKind.OrAcrossColumns);

        Assert.Equal("nome = @nome OR email = @email", Marked(sql, f));
        Assert.DoesNotContain(Analyze("SELECT id FROM clientes WHERE email = 'a' OR email = 'b' LIMIT 5").Findings, x => x.Kind == QueryFindingKind.OrAcrossColumns);
    }

    [Fact] // EXT-PGQ-007
    public void Analyze_FiltroEJuncaoEmColunaSemIndice_CruzaComOEsquemaEOParametroNaoEColuna()
    {
        var sql = "SELECT g.id FROM pedidos p JOIN pagamentos g ON g.pedido_id = p.id WHERE g.pedido_id = @p0 LIMIT 5";

        var f = One(sql, QueryFindingKind.ColumnWithoutIndex);

        Assert.Equal("g.pedido_id", Marked(sql, f));
        Assert.Contains("pagamentos.pedido_id está na junção e não tem índice que comece por ela, nem um composto cujas colunas iniciais estejam todas no filtro com igualdade (o esquema tem pk_pagamentos (id)): o banco pode ler a tabela inteira", f.Message);
    }

    [Fact] // EXT-PGQ-008
    public void Analyze_JuncaoSemCondicao_CrossJoinVirgulaEOnSemColuna()
    {
        var cross = "SELECT p.id FROM pedidos p CROSS JOIN clientes c LIMIT 5";
        Assert.Equal("CROSS JOIN clientes c", Marked(cross, One(cross, QueryFindingKind.JoinWithoutCondition)));

        var comma = "SELECT p.id FROM pedidos p, clientes c WHERE p.id = 1";
        Assert.Equal("clientes c", Marked(comma, One(comma, QueryFindingKind.JoinWithoutCondition)));

        var on = "SELECT p.id FROM pedidos p JOIN clientes c ON p.id = 1 LIMIT 5";
        Assert.Equal("p.id = 1", Marked(on, One(on, QueryFindingKind.JoinWithoutCondition)));
    }

    [Fact] // EXT-PGQ-009
    public void Analyze_DistinctComJuncao_MarcaAPalavraChave()
    {
        var sql = "SELECT DISTINCT c.nome FROM clientes c JOIN pedidos p ON p.cliente_id = c.id LIMIT 5";

        var f = One(sql, QueryFindingKind.DistinctOverJoin);

        Assert.Equal((QuerySeverity.Low, "DISTINCT"), (f.Severity, Marked(sql, f)));
    }

    [Fact] // EXT-PGQ-010
    public void Analyze_SubconsultaCorrelacionada_SugereLateralEExistsNaoConta()
    {
        var sql = "SELECT c.id, (SELECT count(*) FROM pedidos p WHERE p.cliente_id = c.id) AS n FROM clientes c LIMIT 5";

        var f = One(sql, QueryFindingKind.CorrelatedSubquery);

        Assert.Equal("(SELECT count(*) FROM pedidos p WHERE p.cliente_id = c.id)", Marked(sql, f));
        Assert.Contains("LEFT JOIN LATERAL", f.Message);
        Assert.DoesNotContain(Analyze("SELECT c.id FROM clientes c WHERE EXISTS (SELECT 1 FROM pedidos p WHERE p.cliente_id = c.id) LIMIT 5").Findings,
            x => x.Kind == QueryFindingKind.CorrelatedSubquery);
    }

    [Fact] // EXT-PGQ-011
    public void Analyze_OrderBySemLimit_SoNaConsultaDeFora()
    {
        var sql = "SELECT id FROM pedidos ORDER BY criado_em DESC";

        var f = One(sql, QueryFindingKind.OrderByWithoutLimit);

        Assert.Equal("ORDER BY criado_em DESC", Marked(sql, f));
        Assert.Contains("sem LIMIT nem FETCH", f.Message);
    }

    [Fact] // EXT-PGQ-012
    public void Analyze_NotInComColunaQueAceitaNulo_AltaEColunaSemNuloNao()
    {
        var sql = "SELECT id FROM pedidos WHERE criado_em NOT IN (SELECT pago_em FROM pagamentos) LIMIT 5";

        var f = One(sql, QueryFindingKind.NotInNullable);

        Assert.Equal(QuerySeverity.High, f.Severity);
        Assert.Contains("pagamentos.pago_em aceita nulo", f.Message);
        Assert.DoesNotContain(Analyze("SELECT id FROM pedidos WHERE id NOT IN (SELECT pedido_id FROM pagamentos) LIMIT 5").Findings, x => x.Kind == QueryFindingKind.NotInNullable);
    }

    [Fact] // EXT-PGQ-013
    public void Analyze_UpdateEDeleteSemWhere_AltaEComFromOuUsingNao()
    {
        var delete = "DELETE FROM log_eventos";
        Assert.Equal("DELETE FROM log_eventos", Marked(delete, One(delete, QueryFindingKind.WriteWithoutWhere)));

        var update = "UPDATE produtos SET estoque = 0";
        var f = One(update, QueryFindingKind.WriteWithoutWhere);
        Assert.Equal("UPDATE produtos", Marked(update, f));
        Assert.Contains("altera todas as linhas de produtos", f.Message);

        Assert.DoesNotContain(Analyze("DELETE FROM pedidos p USING clientes c WHERE c.id = p.cliente_id AND c.id = 1").Findings, x => x.Kind == QueryFindingKind.WriteWithoutWhere);
        Assert.DoesNotContain(Analyze("UPDATE produtos SET estoque = 0 WHERE id = $1").Findings, x => x.Kind == QueryFindingKind.WriteWithoutWhere);
    }

    [Fact] // EXT-PGQ-014
    public void Analyze_ConsultaDoPrototipoNoPostgreSql_QuatroAchadosComALinhaCerta()
    {
        var sql = """
            SELECT * FROM pedidos p
            JOIN clientes c ON c.id = p.cliente_id
            WHERE p.criado_em::date >= '2026-01-01'
              AND c.email ILIKE '%@gmail.com'
            ORDER BY p.criado_em DESC
            """;

        var findings = Analyze(sql).Findings;

        Assert.Equal([QueryFindingKind.FunctionOnFilterColumn, QueryFindingKind.SelectStar, QueryFindingKind.LeadingWildcardLike, QueryFindingKind.OrderByWithoutLimit],
            findings.Select(f => f.Kind));
        Assert.Equal([3, 1, 4, 5], findings.Select(f => f.Line));
        Assert.Equal(["p.criado_em::date", "*", "c.email ILIKE '%@gmail.com'", "ORDER BY p.criado_em DESC"], findings.Select(f => Marked(sql, f)));
    }

    [Fact] // EXT-PGQ-015
    public void Analyze_TextoQueOLeitorNaoEntende_ErroComLinhaESemAchado()
    {
        var analysis = Analyze("SELECT id FROM pedidos\nWHERE id = = 1");

        Assert.NotNull(analysis.Error);
        Assert.Equal(2, analysis.Error!.Line);
        Assert.Empty(analysis.Findings);
    }

    [Fact] // EXT-PGQ-016
    public void Analyze_TabelaForaDoEsquemaECte_ListadaSemOsAchadosDoEsquema()
    {
        var analysis = Analyze("SELECT e.codigo FROM estoque_central e WHERE e.codigo = 'X1' LIMIT 5");
        Assert.Equal([new QueryTableUse("estoque_central", "e", false)], analysis.Tables);
        Assert.Empty(analysis.Findings);

        var cte = Analyze("WITH recentes AS (SELECT id FROM pedidos WHERE id > 10) SELECT r.id FROM recentes r LIMIT 5");
        Assert.Equal([new QueryTableUse("pedidos", null, true)], cte.Tables);
    }

    [Fact] // EXT-PGQ-017
    public void Analyze_ComentarioEStringComPalavrasDaRegra_NaoEnganam()
    {
        var sql = "SELECT id, 'SELECT * FROM x' AS texto -- SELECT * ORDER BY\nFROM clientes WHERE email = 'a%' LIMIT 1";

        Assert.Empty(Analyze(sql).Findings);
    }

    [Fact] // EXT-PGQ-018
    public void Analyze_LikeSeguidoDeAnd_OPadraoNaoEngoleOResto()
    {
        // O dialeto do leitor lia o AND como parte do padrão do LIKE; com a correção, as duas condições contam.
        var sql = "SELECT c.id FROM clientes c WHERE c.email ILIKE '%@gmail.com' AND c.criado_em::date = $1 AND c.nome LIKE 'a%' LIMIT 5";

        var findings = Analyze(sql).Findings;

        Assert.Equal([QueryFindingKind.FunctionOnFilterColumn, QueryFindingKind.LeadingWildcardLike, QueryFindingKind.ColumnWithoutIndex], findings.Select(f => f.Kind));
        Assert.Equal("c.email ILIKE '%@gmail.com'", Marked(sql, findings[1]));
        Assert.Equal("c.nome", Marked(sql, findings[2]));
        Assert.True(PgSqlPlanGuard.Check("SELECT id FROM clientes WHERE email LIKE '%a' AND id BETWEEN 1 AND 2 OR nome = 'x'").Allowed);
    }
}
