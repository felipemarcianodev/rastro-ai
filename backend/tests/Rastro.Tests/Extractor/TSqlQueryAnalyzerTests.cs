using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-DBQ-NNN: análise de consulta sem IA (Fase 40), uma consulta da loja fictícia LojaBanco
// por achado, cruzando com o esquema lido do código, mais uma consulta limpa.
public class TSqlQueryAnalyzerTests
{
    private static readonly Lazy<DbSchema> Loja = new(() => DbSchemaExtractor.Extract(LojaBanco.Files()));

    private static QueryAnalysis Analyze(string sql) => new TSqlQueryAnalyzer().Analyze(sql, Loja.Value);

    private static QueryFinding One(string sql, QueryFindingKind kind) => Assert.Single(Analyze(sql).Findings, f => f.Kind == kind);

    private static string Marked(string sql, QueryFinding f) => sql.Substring(f.Start, f.Length);

    [Fact] // EXT-DBQ-001
    public void Analyze_ConsultaLimpa_SemAchado()
    {
        var sql = """
            SELECT TOP 20 p.id, p.total, c.nome
            FROM dbo.pedidos p
            JOIN dbo.clientes c ON c.id = p.cliente_id
            WHERE p.cliente_id = @cliente
            ORDER BY p.id DESC
            """;

        var analysis = Analyze(sql);

        Assert.Null(analysis.Error);
        Assert.Equal(1, analysis.Statements);
        Assert.Empty(analysis.Findings);
        Assert.Equal([new QueryTableUse("pedidos", "p", true), new QueryTableUse("clientes", "c", true)], analysis.Tables);
    }

    [Fact] // EXT-DBQ-002
    public void Analyze_SelectEstrela_CitaAsColunasEODadoPessoal()
    {
        var sql = "SELECT * FROM clientes WHERE id = 1";

        var f = One(sql, QueryFindingKind.SelectStar);

        Assert.Equal(QuerySeverity.Medium, f.Severity);
        Assert.Equal("*", Marked(sql, f));
        Assert.Contains("todas as 5 colunas de clientes, inclusive clientes.email e clientes.cpf, marcadas como dado pessoal", f.Message);
        Assert.Equal("clientes", f.Table);
    }

    [Fact] // EXT-DBQ-003
    public void Analyze_FuncaoNaColunaDoFiltro_AltaEDizQueAColunaNemTemIndice()
    {
        var sql = "SELECT TOP 10 p.id FROM pedidos p WHERE YEAR(p.criado_em) = 2026";

        var f = One(sql, QueryFindingKind.FunctionOnFilterColumn);

        Assert.Equal(QuerySeverity.High, f.Severity);
        Assert.Equal("YEAR(p.criado_em)", Marked(sql, f));
        Assert.Contains("YEAR() roda em cada linha e impede o uso de índice em p.criado_em", f.Message);
        Assert.Contains("E pedidos.criado_em nem tem índice.", f.Message);
        // O motivo do índice já está no achado da função: sem um segundo achado para a mesma coluna.
        Assert.DoesNotContain(Analyze(sql).Findings, x => x.Kind == QueryFindingKind.ColumnWithoutIndex);
    }

    [Fact] // EXT-DBQ-004
    public void Analyze_LikeComCuringaNoComeco_DizQueOIndiceExisteMasNaoAjuda()
    {
        var sql = "SELECT TOP 10 c.id FROM clientes c WHERE c.email LIKE '%@gmail.com'";

        var f = One(sql, QueryFindingKind.LeadingWildcardLike);

        Assert.Equal("c.email LIKE '%@gmail.com'", Marked(sql, f));
        Assert.Contains("o índice UX_clientes_email existe, mas não ajuda", f.Message);
        // LIKE sem curinga no começo não é achado.
        Assert.DoesNotContain(Analyze("SELECT TOP 10 id FROM clientes WHERE email LIKE 'ana%'").Findings, x => x.Kind == QueryFindingKind.LeadingWildcardLike);
    }

    [Fact] // EXT-DBQ-005
    public void Analyze_OrEntreColunasDiferentes_ViraAchadoEOrNaMesmaColunaNao()
    {
        var sql = "SELECT TOP 10 id FROM pedidos WHERE cliente_id = 1 OR id = 7";

        var f = One(sql, QueryFindingKind.OrAcrossColumns);

        Assert.Equal("cliente_id = 1 OR id = 7", Marked(sql, f));
        Assert.Contains("(cliente_id e id)", f.Message);
        Assert.Empty(Analyze("SELECT TOP 10 id FROM pedidos WHERE id = 1 OR id = 7").Findings);
    }

    [Fact] // EXT-DBQ-006
    public void Analyze_FiltroEJuncaoEmColunaSemIndice_CruzaComOEsquema()
    {
        var sql = """
            SELECT TOP 10 pg.valor
            FROM pedidos p
            JOIN pagamentos pg ON pg.pedido_id = p.id
            WHERE p.status = 'aberto'
            """;

        var findings = Analyze(sql).Findings.Where(f => f.Kind == QueryFindingKind.ColumnWithoutIndex).ToList();

        Assert.Equal(["pg.pedido_id", "p.status"], findings.Select(f => Marked(sql, f)));
        Assert.Contains("pagamentos.pedido_id está na junção e não tem índice que comece por ela (o esquema tem PK_pagamentos (id))", findings[0].Message);
        Assert.Contains("pedidos.status está no filtro", findings[1].Message);
        Assert.Equal(["pagamentos", "pedidos"], findings.Select(f => f.Table));
    }

    [Fact] // EXT-DBQ-007
    public void Analyze_JuncaoSemCondicao_CrossJoinVirgulaEOnSemColuna()
    {
        var cross = "SELECT TOP 10 c.id FROM clientes c CROSS JOIN produtos pr";
        var f = One(cross, QueryFindingKind.JoinWithoutCondition);
        Assert.Equal(QuerySeverity.High, f.Severity);
        Assert.Equal("CROSS JOIN produtos pr", Marked(cross, f));

        var comma = "SELECT TOP 10 c.id FROM clientes c, pedidos p WHERE c.id = 1";
        Assert.Contains("pedidos entra no FROM pela vírgula", One(comma, QueryFindingKind.JoinWithoutCondition).Message);
        Assert.DoesNotContain(Analyze("SELECT TOP 10 c.id FROM clientes c, pedidos p WHERE p.cliente_id = c.id").Findings, x => x.Kind == QueryFindingKind.JoinWithoutCondition);

        var on = "SELECT TOP 10 c.id FROM clientes c JOIN pedidos p ON 1 = 1";
        Assert.Equal("1 = 1", Marked(on, One(on, QueryFindingKind.JoinWithoutCondition)));
    }

    [Fact] // EXT-DBQ-008
    public void Analyze_DistinctComJuncao_BaixaEMarcaOPalavraChave()
    {
        var sql = "SELECT DISTINCT TOP 10 c.nome FROM clientes c JOIN pedidos p ON p.cliente_id = c.id";

        var f = One(sql, QueryFindingKind.DistinctOverJoin);

        Assert.Equal(QuerySeverity.Low, f.Severity);
        Assert.Equal("DISTINCT", Marked(sql, f));
        Assert.Empty(Analyze("SELECT DISTINCT TOP 10 nome FROM clientes").Findings);
    }

    [Fact] // EXT-DBQ-009
    public void Analyze_SubconsultaCorrelacionada_NaListaDoSelect_EExistsNao()
    {
        var sql = "SELECT TOP 10 c.id, (SELECT COUNT(*) FROM pedidos p WHERE p.cliente_id = c.id) AS pedidos FROM clientes c";

        var f = One(sql, QueryFindingKind.CorrelatedSubquery);

        Assert.StartsWith("(SELECT COUNT(*) FROM pedidos p", Marked(sql, f));
        Assert.Contains("cita c.id, da consulta de fora", f.Message);
        Assert.Empty(Analyze("SELECT TOP 10 c.id FROM clientes c WHERE EXISTS (SELECT 1 FROM pedidos p WHERE p.cliente_id = c.id)").Findings);
    }

    [Fact] // EXT-DBQ-010
    public void Analyze_OrderBySemLimite_SoNaConsultaDeFora()
    {
        var sql = "SELECT id FROM pedidos WHERE cliente_id = 3 ORDER BY id DESC";

        var f = One(sql, QueryFindingKind.OrderByWithoutLimit);

        Assert.Equal("ORDER BY id DESC", Marked(sql, f));
        Assert.Empty(Analyze("SELECT id FROM pedidos WHERE cliente_id = 3 ORDER BY id OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY").Findings);
    }

    [Fact] // EXT-DBQ-011
    public void Analyze_NotInComColunaQueAceitaNulo_AltaEColunaSemNuloNao()
    {
        var sql = "SELECT TOP 10 id FROM clientes WHERE id NOT IN (SELECT pg.pago_em FROM pagamentos pg)";

        var f = One(sql, QueryFindingKind.NotInNullable);

        Assert.Equal(QuerySeverity.High, f.Severity);
        Assert.Contains("pagamentos.pago_em aceita nulo", f.Message);
        Assert.Empty(Analyze("SELECT TOP 10 id FROM pedidos WHERE id NOT IN (SELECT pedido_id FROM itens_pedido)").Findings);
    }

    [Fact] // EXT-DBQ-012
    public void Analyze_UpdateEDeleteSemWhere_AltaEComWhereNao()
    {
        var update = "UPDATE produtos SET estoque = 0";
        var f = One(update, QueryFindingKind.WriteWithoutWhere);
        Assert.Equal(QuerySeverity.High, f.Severity);
        Assert.Equal("UPDATE produtos", Marked(update, f));
        Assert.Contains("UPDATE sem WHERE altera todas as linhas de produtos", f.Message);

        Assert.Contains("DELETE sem WHERE apaga todas as linhas de log_eventos", One("DELETE FROM dbo.log_eventos", QueryFindingKind.WriteWithoutWhere).Message);
        Assert.Empty(Analyze("UPDATE produtos SET estoque = 0 WHERE id = @id").Findings);
    }

    [Fact] // EXT-DBQ-013
    public void Analyze_ConsultaDoPrototipo_QuatroAchadosPorGravidadeComTrechoNoTexto()
    {
        var sql = """
            SELECT * FROM pedidos p
            JOIN clientes c ON c.id = p.cliente_id
            WHERE YEAR(p.criado_em) = 2026
              AND c.email LIKE '%@gmail.com'
            ORDER BY p.criado_em DESC
            """;

        var findings = Analyze(sql).Findings;

        Assert.Equal([QueryFindingKind.FunctionOnFilterColumn, QueryFindingKind.SelectStar, QueryFindingKind.LeadingWildcardLike, QueryFindingKind.OrderByWithoutLimit],
            findings.Select(f => f.Kind));
        Assert.Equal([3, 1, 4, 5], findings.Select(f => f.Line));
        Assert.Contains("todas as 10 colunas de pedidos e clientes", findings[1].Message);
    }

    [Fact] // EXT-DBQ-014
    public void Analyze_TextoQueOLeitorNaoEntende_ErroComLinhaESemAchado()
    {
        var analysis = Analyze("SELECT id FROM pedidos\nWHERE id = = 1");

        Assert.NotNull(analysis.Error);
        Assert.Equal(2, analysis.Error!.Line);
        Assert.Empty(analysis.Findings);
    }

    [Fact] // EXT-DBQ-015
    public void Analyze_TabelaForaDoEsquema_ListadaSemOsAchadosDoEsquema()
    {
        var analysis = Analyze("SELECT TOP 5 e.codigo FROM estoque_central e WHERE e.codigo = 'X1'");

        Assert.Equal([new QueryTableUse("estoque_central", "e", false)], analysis.Tables);
        Assert.Empty(analysis.Findings);
    }
}
