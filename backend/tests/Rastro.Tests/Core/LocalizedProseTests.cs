using System.Text.RegularExpressions;
using Rastro.Core;
using Rastro.Core.Architecture;
using Rastro.Core.Database;
using Rastro.Core.Graph;
using Rastro.Core.Localization;
using Rastro.Core.Prompts;
using Rastro.Core.Surveys;
using Rastro.Core.Validation;

namespace Rastro.Tests.Core;

// Cenários CORE-LANG-1NN: a prosa que o núcleo gera de forma determinística sai no idioma pedido (Fase 50, ADR-009).
// O idioma é parâmetro; o mesmo rascunho dá o mesmo texto, byte a byte, em cada idioma.
public partial class LocalizedProseTests
{
    // Palavras e letras que só o português usa: o inglês e o espanhol não podem tê-las no texto do núcleo.
    [GeneratedRegex(@"[ãõç]|\b(não|fluxo|passo|você|mudança|critérios|chamada|rascunho|levantad\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Portuguese();

    private static readonly UiLanguage[] Foreign = [UiLanguage.English, UiLanguage.Spanish];

    public static TheoryData<UiLanguage> AllLanguages => new(UiLanguages.All);

    public static TheoryData<UiLanguage> ForeignLanguages => new(Foreign);

    // Chave crua: o ResourceManager devolve a própria chave quando falta a tradução.
    // userText: o que o cliente escreveu (intenção do rascunho, regra, nota, id de visão), que fica como está.
    private static void AssertClean(string text, UiLanguage language, params string[] userText)
    {
        Assert.DoesNotMatch(@"\b(Prompt|FlowDoc|Validation|Questions|C4|QueryContext)\.[A-Z]\w+", text);
        if (language == UiLanguage.Portuguese) return;
        var own = userText.Aggregate(text, (current, part) => current.Replace(part, ""));
        Assert.DoesNotMatch(Portuguese(), own);
    }

    private const string DraftIntent = "Registrar a execução na auditoria.";
    private const string ModifiedIntent = "Seguir também quando y.";
    private const string RuleText = "Segue só com x.";

    private static FlowGraph WithFinding(FindingKind kind)
    {
        var graph = ChangePromptTests.Original();
        var node = graph.Nodes.First(n => n.Id == "a") with { Metrics = new MethodMetrics(14, 70, 5, 8) };
        var finding = new Finding(kind, FindingSeverity.Warning, "a", "Job.A tem um problema.", node.Location);
        return graph with { Nodes = [.. graph.Nodes.Select(n => n.Id == "a" ? node : n)], Findings = [finding] };
    }

    [Theory] // CORE-LANG-101
    [MemberData(nameof(AllLanguages))]
    public void ChangePrompt_MesmoRascunho_DaOMesmoTextoNoIdioma(UiLanguage language)
    {
        var context = new PromptContext("loja", "backend/loja.slnx", "Job.Run", "ce7cec50abc");

        var first = ChangePromptBuilder.Build(context, language, ChangePromptTests.Original(), ChangePromptTests.Draft());
        var second = ChangePromptBuilder.Build(context, language, ChangePromptTests.Original(), ChangePromptTests.Draft());

        Assert.Equal(first, second);
        AssertClean(first, language, DraftIntent, ModifiedIntent, RuleText);
        Assert.Contains("`Audit.Registrar`", first);
        Assert.Contains("src/Job.cs:10", first);
        Assert.Contains("ce7cec5", first);
    }

    [Fact] // CORE-LANG-102
    public void ChangePrompt_TresIdiomas_TextosDiferentesComOMesmoCodigoDoCliente()
    {
        var context = new PromptContext("loja", "backend/loja.slnx", "Job.Run", null);

        var texts = UiLanguages.All.ToDictionary(l => l, l => ChangePromptBuilder.Build(context, l, ChangePromptTests.Original(), ChangePromptTests.Draft()));

        Assert.Contains("# Mudança no fluxo `Job.Run`", texts[UiLanguage.Portuguese]);
        Assert.Contains("# Change in the flow `Job.Run`", texts[UiLanguage.English]);
        Assert.Contains("# Cambio en el flujo `Job.Run`", texts[UiLanguage.Spanish]);
        Assert.Equal(3, texts.Values.Distinct().Count());
        Assert.Contains("No changes in the draft.", ChangePromptBuilder.Build(context, UiLanguage.English, ChangePromptTests.Original(), ChangePromptTests.Original()));
    }

    [Theory] // CORE-LANG-103
    [MemberData(nameof(AllLanguages))]
    public void RefactorPrompt_TodoTipoDeAchado_SaiLimpoEDeterministico(UiLanguage language)
    {
        var context = new PromptContext("repo", "App.slnx", "Job.Run", "abcdef123");

        foreach (var kind in Enum.GetValues<FindingKind>())
        {
            var graph = WithFinding(kind);
            var first = RefactorPromptBuilder.Build(context, language, graph, graph.Findings![0]);
            var second = RefactorPromptBuilder.Build(context, language, graph, graph.Findings![0]);

            Assert.Equal(first, second);
            AssertClean(first, language);
            Assert.Contains("src/Job.cs:10", first);
            Assert.Contains("abcdef1", first);
        }
        AssertClean(RefactorPromptBuilder.SeverityText(FindingSeverity.Critical, language), language);
    }

    [Fact] // CORE-LANG-104
    public void RefactorPrompt_Ingles_UsaOsTextosDoIngles()
    {
        var graph = WithFinding(FindingKind.HighComplexity);

        var prompt = RefactorPromptBuilder.Build(new PromptContext("repo", "App.slnx", "Job.Run", null), UiLanguage.English, graph, graph.Findings![0]);

        Assert.Contains("# Refactoring in the flow `Job.Run`", prompt);
        Assert.Contains("Warning: Job.A tem um problema.", prompt);
        Assert.Contains("- Method metrics: complexity 14, 70 lines, 5 levels of nesting and 8 injected dependencies", prompt);
        Assert.Contains("Method cyclomatic complexity of", prompt);
    }

    private static (ChangeValidationResult Result, FlowGraph Draft) Validation(UiLanguage language)
    {
        var original = ChangePromptTests.Original();
        var draft = ChangePromptTests.Draft();
        var (expectations, unexpected) = ChangeValidator.Compare(original, draft, original, language);
        var ok = new ValidationCheck("build", CheckStatus.Passed);
        var skipped = new ValidationCheck("tests", CheckStatus.Skipped, "x");
        var (verdict, score) = ChangeValidator.Judge(expectations, unexpected, ok, skipped);
        return (new ChangeValidationResult(expectations, [.. unexpected, "extra"], 1, 2, ok, skipped, verdict, score), draft);
    }

    [Theory] // CORE-LANG-105
    [MemberData(nameof(AllLanguages))]
    public void ChangeValidator_Criterios_SaemNoIdiomaEDeterministicos(UiLanguage language)
    {
        var original = ChangePromptTests.Original();
        var draft = ChangePromptTests.Draft();

        var first = ChangeValidator.Compare(original, draft, original, language);
        var second = ChangeValidator.Compare(original, draft, original, language);

        Assert.Equal(first.Expectations.Select(c => (c.Description, c.Detail)), second.Expectations.Select(c => (c.Description, c.Detail)));
        Assert.NotEmpty(first.Expectations);
        foreach (var check in first.Expectations)
        {
            AssertClean(check.Description, language);
            if (check.Detail is not null) AssertClean(check.Detail, language);
        }
        foreach (var line in first.Unexpected) AssertClean(line, language);
    }

    [Theory] // CORE-LANG-106
    [MemberData(nameof(AllLanguages))]
    public void FlowDocument_MesmoRascunho_DaOMesmoTextoNoIdioma(UiLanguage language)
    {
        var (result, draft) = Validation(language);
        var flow = ChangePromptTests.Original() with { Nodes = [.. ChangePromptTests.Original().Nodes.Select(n => n.Id == "a" ? n with { Rule = new NodeRule("regra do cliente", ProposalOrigin.Agent) } : n)] };
        var context = new PromptContext("repo", "App.slnx", "Job.Run", "abc");
        var at = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        var notes = new Dictionary<string, string> { ["a"] = "nota do cliente" };

        var first = FlowDocumentBuilder.Build(context, language, flow, ChangePromptTests.Original(), draft, result, "0123456789", at, notes);
        var second = FlowDocumentBuilder.Build(context, language, flow, ChangePromptTests.Original(), draft, result, "0123456789", at, notes);

        Assert.Equal(first, second);
        Assert.Contains("2026-10-16 12:00", first);
        Assert.Contains("regra do cliente", first);
        Assert.Contains("nota do cliente", first);
        Assert.Contains("```mermaid", first);
        // O texto escrito pelo usuário fica como está; o resto sai no idioma.
        AssertClean(first, language, "regra do cliente", "nota do cliente", DraftIntent, ModifiedIntent);
    }

    [Theory] // CORE-LANG-107
    [MemberData(nameof(AllLanguages))]
    public void ValidationSummary_SaiNoIdiomaEDeterministico(UiLanguage language)
    {
        var (result, _) = Validation(language);

        var first = FlowDocumentBuilder.Summary(result, language);
        var second = FlowDocumentBuilder.Summary(result, language);

        Assert.Equal(first, second);
        AssertClean(first.Replace("x", ""), language);
        foreach (var verdict in Enum.GetValues<ValidationVerdict>()) AssertClean(FlowDocumentBuilder.VerdictText(verdict, language), language);
        foreach (var status in Enum.GetValues<CheckStatus>()) AssertClean(FlowDocumentBuilder.StatusText(status, language), language);
    }

    [Fact] // CORE-LANG-108
    public void ValidationSummary_Espanhol_UsaOsTermosDoGlossario()
    {
        var (result, _) = Validation(UiLanguage.Spanish);

        var text = FlowDocumentBuilder.Summary(result, UiLanguage.Spanish);

        Assert.Contains("Compilación: pasó", text);
        Assert.Contains("Pruebas: no configurado (x)", text);
        Assert.Contains("Criterios del borrador:", text);
    }

    [Theory] // CORE-LANG-109
    [MemberData(nameof(AllLanguages))]
    public void FlowQuestions_MesmoGrafo_DaAsMesmasPerguntasNoIdioma(UiLanguage language)
    {
        var graph = FlowMapBuilderTests.Loja();
        var map = FlowMapBuilder.Build(graph);
        var rules = FlowRuleExtractor.Extract(graph, map, language);

        var first = FlowQuestions.Suggest(graph, map, rules, language);
        var second = FlowQuestions.Suggest(graph, map, rules, language);

        Assert.Equal(first.Select(q => (q.Id, q.Text, q.Answer.Text, q.Answer.Gap)), second.Select(q => (q.Id, q.Text, q.Answer.Text, q.Answer.Gap)));
        Assert.NotEmpty(first);
        foreach (var question in first)
        {
            AssertClean(question.Text, language);
            AssertClean(question.Answer.Text, language);
            if (question.Answer.Gap is not null) AssertClean(question.Answer.Gap, language);
        }
        var none = FlowQuestions.Verify(graph, new FlowAnswer("ok", AnswerSeal.Inferred, ["m:inventado"]), language);
        AssertClean(none.Text, language);
        AssertClean(none.Gap!, language);
    }

    [Fact] // CORE-LANG-110
    public void FlowQuestions_Ingles_PerguntaEmIngles()
    {
        var graph = FlowMapBuilderTests.Loja();
        var map = FlowMapBuilder.Build(graph);

        var questions = FlowQuestions.Suggest(graph, map, FlowRuleExtractor.Extract(graph, map, UiLanguage.English), UiLanguage.English);

        Assert.Contains(questions, q => q.Text == "In which situations is the flow refused or ended before the end?");
        Assert.Contains(questions, q => q.Text == "Which systems does the feature go through?");
    }

    [Theory] // CORE-LANG-111
    [MemberData(nameof(AllLanguages))]
    public void C4_MesmoGrafo_DaOMesmoDslNoIdioma(UiLanguage language)
    {
        var graph = FlowMapBuilderTests.Loja();
        var context = new PromptContext("loja", "Loja.slnx", "Pedido.Enviar", "0123456789");
        IReadOnlyList<C4Person> people = [new C4Person("Operador", "Acompanha.", true)];

        var first = C4WorkspaceBuilder.Build(context, language, graph, people);
        var second = C4WorkspaceBuilder.Build(context, language, graph, people);

        Assert.Equal(first, second);
        // Os identificadores das visões não mudam de idioma: a tela e os testes dependem deles.
        Assert.Contains("\"C1-contexto\"", first);
        Assert.Contains("\"C4-fluxo\"", first);
        Assert.Contains($"\"{LocalizedText.Get("C4.Tag.Inferred", language)}\"", first);
        AssertClean(first, language, "Operador", "Acompanha.", "\"C4-fluxo\"");
    }

    [Theory] // CORE-LANG-112
    [MemberData(nameof(AllLanguages))]
    public void PessoasDoC4_Erros_SaemNoIdioma(UiLanguage language)
    {
        var errors = SurveyArchitecture.Validate(
            [.. Enumerable.Range(0, SurveyArchitecture.MaxPeople + 1).Select(_ => new C4Person("", new string('x', SurveyArchitecture.MaxDescriptionLength + 1)))],
            language);

        Assert.True(errors.Count >= 3);
        foreach (var error in errors) AssertClean(error, language);
        Assert.Equal(errors, SurveyArchitecture.Validate(
            [.. Enumerable.Range(0, SurveyArchitecture.MaxPeople + 1).Select(_ => new C4Person("", new string('x', SurveyArchitecture.MaxDescriptionLength + 1)))],
            language));
    }

    [Theory] // CORE-LANG-113
    [MemberData(nameof(AllLanguages))]
    public void QueryContext_AchadosETabelas_SaemNoIdioma(UiLanguage language)
    {
        var findings = Enum.GetValues<QueryFindingKind>()
            .Select((kind, i) => new QueryFinding(kind, (QuerySeverity)(i % 3), 0, 5, i + 1, "SELECT *", "motivo", null))
            .ToList();
        var analysis = new QueryAnalysis(SqlDialect.PostgreSql, 1, findings, [new QueryTableUse("pedidos", null, false)], null);

        var first = QueryContextText.Findings(analysis, language);
        var second = QueryContextText.Findings(analysis, language);

        Assert.Equal(first, second);
        Assert.Equal(findings.Count, first.Split('\n').Length);
        AssertClean(first.Replace("motivo", ""), language);
        var schema = new DbSchema(SqlDialect.PostgreSql, [], [], [], [], [], []);
        var tables = QueryContextText.Tables(analysis, schema, language);
        Assert.Contains("pedidos", tables);
        AssertClean(tables, language);
    }
}
