using System.Text.RegularExpressions;
using Rastro.Core.Database;
using Rastro.Core.Graph;
using Rastro.Core.Localization;
using Rastro.Extractor.Database;
using Rastro.Infrastructure.Database.Live;

namespace Rastro.Tests.Localization;

// Cenários DB-LANG-NNN: os textos da visão do banco saem no idioma do pedido (Fase 50, ADR-009).
public partial class DatabaseLanguageTests
{
    private static readonly Lazy<DbSchema> Loja = new(() => DbSchemaExtractor.Extract(Rastro.Tests.Extractor.LojaBanco.Files(), null, UiLanguage.English));
    private static readonly Lazy<DbSchema> LojaPg = new(() => DbSchemaExtractor.Extract(Rastro.Tests.Extractor.LojaBancoPg.Files(), null, UiLanguage.English));

    // Chave de recurso que vazou como texto: "Query.AlgumaCoisa".
    [GeneratedRegex(@"^(Query|Sql|Pg|Guard|Check|Schema|Live|Plan|Advice)\.[A-Za-z0-9.]+$")]
    private static partial Regex RawKey();

    private const string Star = "SELECT * FROM pedidos WHERE YEAR(criado_em) = 2026";

    [Theory] // DB-LANG-001
    [InlineData(UiLanguage.English, "Brings back all", "runs on every row")]
    [InlineData(UiLanguage.Portuguese, "Traz todas as", "roda em cada linha")]
    [InlineData(UiLanguage.Spanish, "Trae las", "se ejecuta en cada fila")]
    public void TSqlAnalyzer_MesmaConsulta_DaOsAchadosNoIdiomaPedido(UiLanguage language, string star, string function)
    {
        var analysis = new TSqlQueryAnalyzer().Analyze(Star, Loja.Value, language);

        Assert.Contains(analysis.Findings, f => f.Kind == QueryFindingKind.SelectStar && f.Message.StartsWith(star, StringComparison.Ordinal));
        Assert.Contains(analysis.Findings, f => f.Kind == QueryFindingKind.FunctionOnFilterColumn && f.Message.Contains(function, StringComparison.Ordinal));
        Assert.DoesNotContain(analysis.Findings, f => RawKey().IsMatch(f.Message));
        // O que o usuário vê do SQL não muda com o idioma: o trecho marcado e a gravidade são os mesmos.
        var portuguese = new TSqlQueryAnalyzer().Analyze(Star, Loja.Value, UiLanguage.Portuguese);
        Assert.Equal(portuguese.Findings.Select(f => (f.Kind, f.Severity, f.Start, f.Length, f.Snippet)),
            analysis.Findings.Select(f => (f.Kind, f.Severity, f.Start, f.Length, f.Snippet)));
    }

    [Theory] // DB-LANG-002
    [InlineData(UiLanguage.English, "Sorts every row the filter left")]
    [InlineData(UiLanguage.Portuguese, "Ordena todas as linhas que o filtro deixou")]
    [InlineData(UiLanguage.Spanish, "Ordena todas las filas que dejó el filtro")]
    public void PgAnalyzer_MesmaConsulta_DaOsAchadosNoIdiomaPedido(UiLanguage language, string order)
    {
        var analysis = new PgSqlQueryAnalyzer().Analyze("SELECT id FROM pedidos WHERE status = 'x' ORDER BY criado_em", LojaPg.Value, language);

        Assert.Contains(analysis.Findings, f => f.Kind == QueryFindingKind.OrderByWithoutLimit && f.Message.StartsWith(order, StringComparison.Ordinal));
        Assert.DoesNotContain(analysis.Findings, f => RawKey().IsMatch(f.Message));
    }

    [Theory] // DB-LANG-003
    [InlineData(UiLanguage.English, "Incorrect syntax near 'FROM'.")]
    [InlineData(UiLanguage.Portuguese, "sintaxe incorreta perto de 'FROM'")]
    [InlineData(UiLanguage.Spanish, "sintaxis incorrecta cerca de 'FROM'")]
    public void TSqlAnalyzer_ConsultaQueNaoLe_ErroNoIdiomaPedido(UiLanguage language, string expected)
    {
        var error = new TSqlQueryAnalyzer().Analyze("SELECT FROM WHERE", Loja.Value, language).Error;

        Assert.Equal(expected, error!.Message);
    }

    [Theory] // DB-LANG-004
    [InlineData(UiLanguage.English, "The rewrite references the table")]
    [InlineData(UiLanguage.Portuguese, "A reescrita cita a tabela")]
    [InlineData(UiLanguage.Spanish, "La reescritura cita la tabla")]
    public void Checkers_ReescritaComTabelaDeFora_RecusaNoIdiomaPedido(UiLanguage language, string expected)
    {
        var proposal = new QueryProposal("SELECT id FROM tabela_inexistente", [], [], ProposalOrigin.Agent);

        var tsql = new TSqlProposalChecker().Review("SELECT id FROM pedidos", proposal, Loja.Value, language);
        var pg = new PgSqlProposalChecker().Review("SELECT id FROM pedidos", proposal, LojaPg.Value, language);

        Assert.All(new[] { tsql, pg }, review =>
        {
            Assert.False(review.Rewrite!.Accepted);
            Assert.Contains(review.Rewrite.Refusals, r => r.StartsWith(expected, StringComparison.Ordinal));
            Assert.DoesNotContain(review.Rewrite.Refusals, r => RawKey().IsMatch(r));
        });
    }

    [Theory] // DB-LANG-005
    [InlineData(UiLanguage.English, "The estimated plan is only produced for SELECT")]
    [InlineData(UiLanguage.Portuguese, "O plano estimado só sai para SELECT")]
    [InlineData(UiLanguage.Spanish, "El plan estimado solo se genera para SELECT")]
    public void PlanGuards_InstrucaoQueNaoVaiAoBanco_MotivoNoIdiomaPedido(UiLanguage language, string expected)
    {
        var tsql = TSqlPlanGuard.Check("SET SHOWPLAN_XML OFF", language);
        var pg = PgSqlPlanGuard.Check("SET search_path TO x", language);

        Assert.False(tsql.Allowed);
        Assert.False(pg.Allowed);
        Assert.StartsWith(expected, tsql.Reason, StringComparison.Ordinal);
        Assert.StartsWith(expected, pg.Reason, StringComparison.Ordinal);
    }

    [Theory] // DB-LANG-006
    [InlineData(UiLanguage.English, "Incorrect syntax near ','.", "Incorrect syntax near ','.")]
    [InlineData(UiLanguage.Portuguese, "Incorrect syntax near ','.", "sintaxe incorreta perto de ','")]
    [InlineData(UiLanguage.Spanish, "Incorrect syntax near ','.", "sintaxis incorrecta cerca de ','")]
    [InlineData(UiLanguage.Spanish, "Expected an expression, found =", "esperaba una expresión, pero encontró =")]
    [InlineData(UiLanguage.English, "Expected an expression, found =", "Expected an expression, found =")]
    [InlineData(UiLanguage.Spanish, "Something else entirely", "Something else entirely")]
    public void SqlMessageTranslator_Idioma_InglesDevolveOriginalEEspanholTraduz(UiLanguage language, string message, string expected) =>
        Assert.Equal(expected, SqlMessageTranslator.Translate(message, language));

    [Fact] // DB-LANG-007
    public void TableSize_SeparadorDeMilharVemDaCulturaDoIdioma()
    {
        Assert.Equal("250.000", TableSize.Format(250_000, UiLanguage.Portuguese));
        Assert.Equal("250,000", TableSize.Format(250_000, UiLanguage.English));
        Assert.Equal("250.000", TableSize.Format(250_000, UiLanguage.Spanish));
        Assert.Equal("42", TableSize.Format(42, UiLanguage.English));
    }

    [Theory] // DB-LANG-008
    [InlineData(UiLanguage.English, "Connected: 1 table in the catalog.")]
    [InlineData(UiLanguage.Portuguese, "Conectou: 1 tabela no catálogo.")]
    [InlineData(UiLanguage.Spanish, "Conectó: 1 tabla en el catálogo.")]
    public void LiveTexts_ChaveConhecida_SaiNoIdioma(UiLanguage language, string expected) =>
        Assert.Equal(expected, Rastro.Infrastructure.Resources.Database.Get("Live.TestOkOne", language));

    [Fact] // DB-LANG-009
    public void ShowPlanReader_AvisoDoPlano_SaiNoIdioma()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan"><BatchSequence><Batch><Statements>
              <StmtSimple StatementType="SELECT" StatementSubTreeCost="1"><QueryPlan><Warnings><NoJoinPredicate /></Warnings></QueryPlan></StmtSimple>
            </Statements></Batch></BatchSequence></ShowPlanXML>
            """;

        Assert.Equal("Join without a predicate: the database joins every row with every row.", Assert.Single(ShowPlanReader.Read(xml, UiLanguage.English).Warnings));
        Assert.Equal("Junção sem predicado: o banco junta cada linha com cada linha.", Assert.Single(ShowPlanReader.Read(xml, UiLanguage.Portuguese).Warnings));
        Assert.Equal("JOIN sin predicado: la base de datos junta cada fila con cada fila.", Assert.Single(ShowPlanReader.Read(xml, UiLanguage.Spanish).Warnings));
    }

    [Fact] // DB-LANG-010
    public void Fontes_TodaChaveDeTextoDoBancoExisteNoResx()
    {
        var src = Path.Combine(TestPaths.BackendRoot(), "src");
        var pairs = new (string Resx, string[] Sources)[]
        {
            (Path.Combine(src, "Rastro.Extractor", "Resources", "Database.resx"), [Path.Combine(src, "Rastro.Extractor", "Database")]),
            (Path.Combine(src, "Rastro.Infrastructure", "Resources", "Database.resx"),
                [Path.Combine(src, "Rastro.Infrastructure", "Database"), Path.Combine(src, "Rastro.Api", "Controllers"), Path.Combine(src, "Rastro.Ai", "Suggestions")])
        };
        var used = new Regex("""(?:Txt\.Get|\bT)\("((?:Query|Sql|Pg|Guard|Check|Schema|Live|Plan|Advice)\.[A-Za-z0-9.]+)""");

        foreach (var (resx, folders) in pairs)
        {
            var keys = System.Xml.Linq.XDocument.Load(resx).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
            var found = folders.SelectMany(f => Directory.EnumerateFiles(f, "*.cs", SearchOption.AllDirectories))
                .SelectMany(file => used.Matches(File.ReadAllText(file)).Select(m => (file, key: m.Groups[1].Value)))
                .ToList();
            Assert.NotEmpty(found);
            Assert.All(found, f => Assert.True(keys.Contains(f.key), $"Chave {f.key} de {Path.GetFileName(f.file)} não está em {Path.GetFileName(resx)}"));
        }
    }
}
