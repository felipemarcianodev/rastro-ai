using Rastro.Core.Database;
using Rastro.Core.Graph;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-DBA-NNN: conferência da proposta de melhora de consulta (Fase 41), sem IA, com a loja
// fictícia LojaBanco. A proposta é escrita à mão, no lugar da resposta da IA.
public class TSqlProposalCheckerTests
{
    private static readonly Lazy<DbSchema> Loja = new(() => DbSchemaExtractor.Extract(LojaBanco.Files()));

    private const string Prototype = """
        SELECT * FROM pedidos p
        JOIN clientes c ON c.id = p.cliente_id
        WHERE YEAR(p.criado_em) = 2026
          AND c.email LIKE '%@gmail.com'
        ORDER BY p.criado_em DESC
        """;

    private static QueryProposalReview Review(string original, string? rewritten, params IndexSuggestion[] indexes) =>
        new TSqlProposalChecker().Review(original, new QueryProposal(rewritten, [new QueryChange("Trocou YEAR por faixa", "Usa o índice")], indexes, ProposalOrigin.Provider), Loja.Value);

    private static RewriteReview Rewrite(string original, string rewritten) => Review(original, rewritten).Rewrite!;

    private static string ColumnsOf(string table, string alias) =>
        string.Join(", ", Loja.Value.Tables.Single(t => t.Name == table).Columns.Select(c => $"{alias}.{c.Name}"));

    private static IndexSuggestion Index(string table, string[] columns, string[]? include = null, bool unique = false) =>
        new(table, columns, include ?? [], unique, "Ajuda o filtro.");

    [Fact] // EXT-DBA-001
    public void Review_ReescritaDoPrototipo_AceitaComMenosAchados()
    {
        var rewritten = $"""
            SELECT {ColumnsOf("pedidos", "p")}, {ColumnsOf("clientes", "c")}
            FROM pedidos p
            JOIN clientes c ON c.id = p.cliente_id
            WHERE p.criado_em >= '2026-01-01' AND p.criado_em < DATEADD(year, 1, '2026-01-01')
              AND c.email LIKE '%@gmail.com'
            ORDER BY p.criado_em DESC
            """;

        var review = Review(Prototype, rewritten);

        Assert.Equal(ProposalOrigin.Provider, review.Origin);
        Assert.Equal(4, review.FindingsBefore);
        Assert.True(review.Rewrite!.Accepted, string.Join(" | ", review.Rewrite.Refusals));
        Assert.True(review.Rewrite.After!.Findings.Count < 4);
        Assert.DoesNotContain(review.Rewrite.After.Findings, f => f.Kind is QueryFindingKind.SelectStar or QueryFindingKind.FunctionOnFilterColumn);
        Assert.Equal("Trocou YEAR por faixa", Assert.Single(review.Changes).What);
    }

    [Fact] // EXT-DBA-002
    public void Review_SemReescritaOuIgualAOriginal_SemConferenciaDeReescrita()
    {
        Assert.Null(Review(Prototype, null).Rewrite);
        Assert.Null(Review(Prototype, "  ").Rewrite);
        Assert.Null(Review("SELECT id FROM pedidos", "SELECT id\n  FROM pedidos").Rewrite);
    }

    [Fact] // EXT-DBA-003
    public void Review_ReescritaQueOLeitorNaoEntende_RecusadaComLinha()
    {
        var rewrite = Rewrite("SELECT id FROM pedidos", "SELECT id FROM pedidos WHERE id = = 1");

        Assert.False(rewrite.Accepted);
        Assert.Null(rewrite.After);
        Assert.StartsWith("O leitor de T-SQL não entendeu a consulta reescrita (linha 1", Assert.Single(rewrite.Refusals));
    }

    [Fact] // EXT-DBA-004
    public void Review_ReescritaComSetOuDuasInstrucoes_RecusadaPelaPortaDoBancoVivo()
    {
        var hidden = Rewrite("SELECT id FROM pedidos", "SET SHOWPLAN_XML OFF; SELECT id FROM pedidos");
        var exec = Rewrite("SELECT id FROM pedidos", "EXEC dbo.sp_baixar_estoque 1, 2");

        Assert.Contains("uma instrução só", Assert.Single(hidden.Refusals));
        Assert.Contains("não passa pela conferência do Rastro", Assert.Single(exec.Refusals));
    }

    [Fact] // EXT-DBA-005
    public void Review_ReescritaQueMudaAOperacaoOuOAlvo_Recusada()
    {
        var other = Rewrite("SELECT id FROM pedidos", "DELETE FROM pedidos WHERE id = 1");
        var target = Rewrite("UPDATE pedidos SET status = 'pago' WHERE id = 1", "UPDATE clientes SET nome = 'x' WHERE id = 1");

        Assert.Contains("A consulta original é um SELECT e a reescrita é um DELETE", other.Refusals[0]);
        Assert.Contains("A consulta original muda pedidos e a reescrita muda clientes", target.Refusals[0]);
    }

    [Fact] // EXT-DBA-006
    public void Review_TabelaOuColunaQueNaoExiste_RecusadaComOMotivo()
    {
        var table = Rewrite("SELECT p.id FROM pedidos p", "SELECT p.id FROM pedidos_arquivo p");
        var qualified = Rewrite("SELECT p.id FROM pedidos p", "SELECT p.id FROM pedidos p WHERE p.desconto > 0");
        var loose = Rewrite("SELECT p.id FROM pedidos p", "SELECT p.id FROM pedidos p WHERE desconto > 0");
        var alias = Rewrite("SELECT p.id FROM pedidos p", "SELECT p.id FROM pedidos p WHERE x.id > 0");

        Assert.Contains("A reescrita cita a tabela pedidos_arquivo, que não está no esquema lido do código.", table.Refusals);
        Assert.Contains("A reescrita cita p.desconto, e a coluna desconto não existe em pedidos.", qualified.Refusals);
        Assert.Contains(loose.Refusals, r => r.StartsWith("A reescrita cita a coluna desconto, que não existe em nenhuma tabela da consulta"));
        Assert.Contains("A reescrita cita x.id, mas x não é tabela nem apelido da consulta.", alias.Refusals);
    }

    [Fact] // EXT-DBA-007
    public void Review_ColunasDevolvidasDiferentes_Recusada()
    {
        var fewer = Rewrite(Prototype, "SELECT p.id, p.total FROM pedidos p JOIN clientes c ON c.id = p.cliente_id WHERE p.criado_em >= '2026-01-01'");
        var renamed = Rewrite("SELECT p.id, p.total FROM pedidos p", "SELECT p.id, p.total AS valor FROM pedidos p");

        Assert.Contains(fewer.Refusals, r => r.Contains("devolve 10 colunas") && r.Contains("a reescrita devolve 2 colunas"));
        Assert.Contains("A coluna 2 muda: a original devolve total e a reescrita devolve valor. Mantenha os mesmos nomes, na mesma ordem.", renamed.Refusals);
    }

    [Fact] // EXT-DBA-012
    public void Review_ReescritaQueLimitaAsLinhas_Recusada()
    {
        var top = Rewrite("SELECT p.id FROM pedidos p ORDER BY p.id", "SELECT TOP 50 p.id FROM pedidos p ORDER BY p.id");
        var page = Rewrite("SELECT p.id FROM pedidos p ORDER BY p.id", "SELECT p.id FROM pedidos p ORDER BY p.id OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY");
        var kept = Rewrite("SELECT TOP 10 p.id FROM pedidos p WHERE YEAR(p.criado_em) = 2026", "SELECT TOP 10 p.id FROM pedidos p WHERE p.criado_em >= '2026-01-01' AND p.criado_em < '2027-01-01'");

        Assert.Contains(top.Refusals, r => r.StartsWith("A reescrita limita as linhas"));
        Assert.Contains(page.Refusals, r => r.StartsWith("A reescrita limita as linhas"));
        Assert.True(kept.Accepted, string.Join(" | ", kept.Refusals));
    }

    [Fact] // EXT-DBA-008
    public void Review_ParteDaDataEColunaQueAOriginalJaCitava_NaoRecusam()
    {
        // desconto não está no esquema, mas a original já o citava: o esquema do código pode estar atrás do banco.
        var rewrite = Rewrite(
            "SELECT p.id, p.desconto FROM pedidos p WHERE YEAR(p.criado_em) = 2026",
            "SELECT p.id, p.desconto FROM pedidos p WHERE p.criado_em >= DATEFROMPARTS(2026, 1, 1) AND p.criado_em < DATEADD(year, 1, DATEFROMPARTS(2026, 1, 1)) ORDER BY DATEDIFF(day, p.criado_em, GETDATE())");

        Assert.True(rewrite.Accepted, string.Join(" | ", rewrite.Refusals));
    }

    [Fact] // EXT-DBA-009
    public void Review_UpdateComApelidoECte_Aceita()
    {
        var update = Rewrite(
            "UPDATE pedidos SET status = 'cancelado' WHERE YEAR(criado_em) < 2020",
            "UPDATE p SET status = 'cancelado' FROM pedidos p WHERE p.criado_em < '2020-01-01'");
        var cte = Rewrite(
            "SELECT p.id FROM pedidos p WHERE p.cliente_id IN (SELECT c.id FROM clientes c WHERE c.cpf IS NULL)",
            "WITH sem_cpf AS (SELECT c.id FROM clientes c WHERE c.cpf IS NULL) SELECT p.id FROM pedidos p JOIN sem_cpf s ON s.id = p.cliente_id");

        Assert.True(update.Accepted, string.Join(" | ", update.Refusals));
        Assert.True(cte.Accepted, string.Join(" | ", cte.Refusals));
    }

    [Fact] // EXT-DBA-010
    public void Review_IndiceComColunasQueExistem_AceitoComOComandoDoRastro()
    {
        var index = Assert.Single(Review(Prototype, null, Index("dbo.pedidos", ["criado_em"], ["total", "criado_em"])).Indexes);

        Assert.True(index.Accepted, index.Refusal);
        Assert.Equal(["criado_em"], index.Columns);
        Assert.Equal(["total"], index.Include);
        Assert.Equal("CREATE INDEX [IX_pedidos_criado_em] ON [pedidos] ([criado_em]) INCLUDE ([total]);", index.CreateSql);
    }

    [Fact] // EXT-DBA-011
    public void Review_IndiceQueRepeteOuNaoExiste_RecusadoComOMotivo()
    {
        var indexes = Review(Prototype, null,
            Index("pedidos", ["cliente_id"]),
            Index("pedidos", ["id"]),
            Index("pedidos", ["desconto"]),
            Index("pedidos_arquivo", ["id"]),
            Index("vw_pedidos_por_cliente", ["cliente_id"]),
            Index("clientes", ["criado_em"]),
            Index("clientes", ["criado_em"])).Indexes;

        Assert.Equal(5, indexes.Count);
        Assert.Equal("Já existe o índice IX_pedidos_cliente_id (cliente_id), que começa por essas colunas.", indexes[0].Refusal);
        Assert.Equal("Já existe a chave primária pk_pedidos (id), que começa por essas colunas.", indexes[1].Refusal);
        Assert.Equal("A coluna desconto não existe em pedidos.", indexes[2].Refusal);
        Assert.Equal("A tabela pedidos_arquivo não está no esquema lido do código.", indexes[3].Refusal);
        Assert.Equal("vw_pedidos_por_cliente é uma view: o índice vai na tabela.", indexes[4].Refusal);
        Assert.All(indexes, i => Assert.Null(i.CreateSql));

        var repeated = Review(Prototype, null, Index("clientes", ["criado_em"]), Index("clientes", ["CRIADO_EM"])).Indexes;
        Assert.True(repeated[0].Accepted);
        Assert.Equal("Repete outro índice desta proposta.", repeated[1].Refusal);
    }
}
