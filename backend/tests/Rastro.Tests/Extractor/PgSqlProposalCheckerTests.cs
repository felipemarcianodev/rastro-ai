using Rastro.Core.Database;
using Rastro.Core.Graph;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-PGA-NNN (conferência da proposta da IA no PostgreSQL) e EXT-PGP-NNN (porta da consulta
// para o banco vivo do PostgreSQL), sem IA, com a loja fictícia LojaBancoPg (Fase 42). A proposta é
// escrita à mão, no lugar da resposta da IA.
public class PgSqlProposalCheckerTests
{
    private static readonly Lazy<DbSchema> Loja = new(() => DbSchemaExtractor.Extract(LojaBancoPg.Files()));

    private const string Prototype = """
        SELECT * FROM pedidos p
        JOIN clientes c ON c.id = p.cliente_id
        WHERE p.criado_em::date >= '2026-01-01'
          AND c.email ILIKE '%@gmail.com'
        ORDER BY p.criado_em DESC
        """;

    private static QueryProposalReview Review(string original, string? rewritten, params IndexSuggestion[] indexes) =>
        new PgSqlProposalChecker().Review(original, new QueryProposal(rewritten, [new QueryChange("Trocou a conversão por faixa", "Usa o índice")], indexes, ProposalOrigin.Agent), Loja.Value);

    private static RewriteReview Rewrite(string original, string rewritten) => Review(original, rewritten).Rewrite!;

    private static string ColumnsOf(string table, string alias) =>
        string.Join(", ", Loja.Value.Tables.Single(t => t.Name == table).Columns.Select(c => $"{alias}.{c.Name}"));

    private static IndexSuggestion Index(string table, string[] columns, string[]? include = null, bool unique = false) =>
        new(table, columns, include ?? [], unique, "Ajuda o filtro.");

    [Fact] // EXT-PGA-001
    public void Review_ReescritaDoPrototipo_AceitaComMenosAchados()
    {
        var rewritten = $"""
            SELECT {ColumnsOf("pedidos", "p")}, {ColumnsOf("clientes", "c")}
            FROM pedidos p
            JOIN clientes c ON c.id = p.cliente_id
            WHERE p.criado_em >= '2026-01-01'::timestamptz
              AND c.email ILIKE '%@gmail.com'
            ORDER BY p.criado_em DESC
            """;

        var review = Review(Prototype, rewritten);

        Assert.Equal(4, review.FindingsBefore);
        Assert.True(review.Rewrite!.Accepted, string.Join(" | ", review.Rewrite.Refusals));
        Assert.Equal(SqlDialect.PostgreSql, review.Rewrite.After!.Dialect);
        Assert.DoesNotContain(review.Rewrite.After.Findings, f => f.Kind is QueryFindingKind.SelectStar or QueryFindingKind.FunctionOnFilterColumn);
    }

    [Fact] // EXT-PGA-002
    public void Review_ReescritaQueLimitaAsLinhas_RecusadaComLimitEFetch()
    {
        var original = "SELECT id FROM pedidos ORDER BY criado_em DESC";

        Assert.Contains("A reescrita limita as linhas (LIMIT, FETCH ou OFFSET)", Assert.Single(Rewrite(original, "SELECT id FROM pedidos ORDER BY criado_em DESC LIMIT 50").Refusals));
        Assert.False(Rewrite(original, "SELECT id FROM pedidos ORDER BY criado_em DESC FETCH FIRST 50 ROWS ONLY").Accepted);
    }

    [Fact] // EXT-PGA-003
    public void Review_ColunasDiferentesOuOutraOperacao_Recusada()
    {
        var columns = Rewrite("SELECT id, total FROM pedidos WHERE id = 1", "SELECT id FROM pedidos WHERE id = 1");
        Assert.Contains("A consulta original devolve 2 colunas (id, total) e a reescrita devolve 1 coluna", Assert.Single(columns.Refusals));

        // O PostgreSQL dá o nome da função à coluna sem apelido: count e sum são nomes diferentes.
        var named = Rewrite("SELECT count(id) FROM pedidos WHERE id > 1", "SELECT sum(id) FROM pedidos WHERE id > 1");
        Assert.Contains("a original devolve count e a reescrita devolve sum", Assert.Single(named.Refusals));
        Assert.True(Rewrite("SELECT p.total::text FROM pedidos p WHERE p.id = 1", "SELECT CAST(p.total AS text) FROM pedidos p WHERE p.id = 1").Accepted);

        var operation = Rewrite("DELETE FROM pedidos WHERE id = 1", "SELECT id FROM pedidos WHERE id = 1");
        Assert.Contains("A consulta original é um DELETE e a reescrita é um SELECT", Assert.Single(operation.Refusals));

        var table = Rewrite("UPDATE pedidos SET status = 'pago' WHERE id = 1", "UPDATE pagamentos SET valor = 0 WHERE id = 1");
        Assert.Contains("A consulta original muda pedidos e a reescrita muda pagamentos", table.Refusals[0]);
    }

    [Fact] // EXT-PGA-004
    public void Review_TabelaOuColunaQueNaoExiste_Recusada()
    {
        var table = Rewrite("SELECT id FROM pedidos WHERE id = 1", "SELECT id FROM pedidos_novos WHERE id = 1");
        Assert.Contains("A reescrita cita a tabela pedidos_novos, que não está no esquema lido do código.", table.Refusals);

        var column = Rewrite("SELECT p.id FROM pedidos p WHERE p.id = 1", "SELECT p.id FROM pedidos p WHERE p.numero = 1");
        Assert.Contains("A reescrita cita p.numero, e a coluna numero não existe em pedidos.", column.Refusals);

        var written = Rewrite("UPDATE pedidos SET status = 'pago' WHERE id = 1", "UPDATE pedidos SET situacao = 'pago' WHERE id = 1");
        Assert.Contains("A reescrita grava a coluna situacao, que não existe em pedidos.", written.Refusals);

        // O @p0 do Npgsql não é coluna.
        Assert.True(Rewrite("SELECT id FROM pedidos WHERE cliente_id = @p0 AND id > 0", "SELECT id FROM pedidos WHERE cliente_id = @p0").Accepted);
    }

    [Fact] // EXT-PGA-005
    public void Review_ReescritaQueNaoPassaPelaPorta_Recusada()
    {
        var two = Rewrite("SELECT id FROM pedidos WHERE id = 1", "SELECT id FROM pedidos WHERE id = 1; DELETE FROM pedidos");
        Assert.Contains("uma instrução só", Assert.Single(two.Refusals));

        var sleep = Rewrite("SELECT id FROM pedidos WHERE id = 1", "SELECT id FROM pedidos WHERE id = 1 AND pg_sleep(10) IS NOT NULL");
        Assert.Contains("A função pg_sleep tem efeito fora da consulta", Assert.Single(sleep.Refusals));

        var unreadable = Rewrite("SELECT id FROM pedidos WHERE id = 1", "SELECT id FROM pedidos WHERE id = = 1");
        Assert.StartsWith("O leitor de SQL do PostgreSQL não entendeu a consulta reescrita", Assert.Single(unreadable.Refusals));
    }

    [Fact] // EXT-PGA-006
    public void Review_Indices_ComandoComAspasDuplasEOsRecusadosComOMotivo()
    {
        var review = Review(Prototype, null,
            Index("pedidos", ["criado_em"], ["total"]),
            Index("pedidos", ["id"]),
            Index("vw_pedidos_por_cliente", ["nome"]),
            Index("pedidos", ["numero"]),
            Index("public.clientes", ["email", "nome"]));

        var indexes = review.Indexes;
        Assert.True(indexes[0].Accepted);
        Assert.Equal("CREATE INDEX \"ix_pedidos_criado_em\" ON \"pedidos\" (\"criado_em\") INCLUDE (\"total\");", indexes[0].CreateSql);
        Assert.Equal("Já existe a chave primária pk_pedidos (id), que começa por essas colunas.", indexes[1].Refusal);
        Assert.Equal("vw_pedidos_por_cliente é uma view: o índice vai na tabela.", indexes[2].Refusal);
        Assert.Equal("A coluna numero não existe em pedidos.", indexes[3].Refusal);
        Assert.True(indexes[4].Accepted);
        Assert.Equal("CREATE INDEX \"ix_clientes_email_nome\" ON \"clientes\" (\"email\", \"nome\");", indexes[4].CreateSql);
    }

    [Fact] // EXT-PGP-001
    public void PlanGuard_UmaInstrucaoDeLeituraOuEscrita_PassaComOsParametros()
    {
        var select = PgSqlPlanGuard.Check("SELECT id FROM pedidos WHERE cliente_id = @cliente AND id > $1 AND status = $2");
        Assert.True(select.Allowed, select.Reason);
        Assert.Equal(["@cliente", "$1", "$2"], select.Variables);

        Assert.True(PgSqlPlanGuard.Check("UPDATE produtos SET estoque = estoque - 1 WHERE id = $1").Allowed);
        Assert.True(PgSqlPlanGuard.Check("DELETE FROM log_eventos WHERE ocorrido_em < now()").Allowed);
        Assert.True(PgSqlPlanGuard.Check("INSERT INTO log_eventos (ocorrido_em, mensagem) SELECT now(), 'x'").Allowed);
        Assert.True(SqlPlanGuards.Check(SqlDialect.PostgreSql, "SELECT 1").Allowed);
    }

    [Theory] // EXT-PGP-002
    [InlineData("SELECT 1; SELECT 2", "uma instrução só")]
    [InlineData("SET statement_timeout = 0", "SET, COPY, CALL, DO, EXPLAIN e DDL")]
    [InlineData("EXPLAIN ANALYZE SELECT 1", "SET, COPY, CALL, DO, EXPLAIN e DDL")]
    [InlineData("COPY pedidos TO '/tmp/pedidos.csv'", "SET, COPY, CALL, DO, EXPLAIN e DDL")]
    [InlineData("CALL arquivar_pedidos(now())", "SET, COPY, CALL, DO, EXPLAIN e DDL")]
    [InlineData("DROP TABLE pedidos", "SET, COPY, CALL, DO, EXPLAIN e DDL")]
    [InlineData("SELECT id INTO copia FROM pedidos", "SELECT INTO cria uma tabela")]
    [InlineData("SELECT pg_sleep(30)", "A função pg_sleep")]
    [InlineData("SELECT set_config('default_transaction_read_only', 'off', false)", "A função set_config")]
    [InlineData("SELECT nextval('pedidos_id_seq')", "A função nextval")]
    [InlineData("SELECT * FROM dblink('host=x', 'DELETE FROM y') AS t(id int)", "não entendeu")]
    [InlineData("SELECT pg_catalog.pg_read_file('/etc/passwd')", "A função pg_read_file")]
    [InlineData("SELECT lo_import('/etc/passwd')", "A função lo_import")]
    [InlineData("WITH d AS (DELETE FROM pedidos RETURNING id) SELECT id FROM d", "não entendeu")]
    [InlineData("DO $$ BEGIN DELETE FROM pedidos; END $$", "não entendeu")]
    public void PlanGuard_ForaDaPorta_RecusadaComOMotivo(string sql, string reason)
    {
        var result = PgSqlPlanGuard.Check(sql);

        Assert.False(result.Allowed);
        Assert.Contains(reason, result.Reason);
    }
}
