using Rastro.Core;
using Rastro.Core.Graph;
using Rastro.Core.Prompts;
using Rastro.Core.Validation;

namespace Rastro.Tests.Core;

// Cenários CORE-VAL-NNN: comparação do fluxo novo com o rascunho, nota e documentação.
public class ChangeValidatorTests
{
    private static FlowNode Method(string id, string label) => new(id, NodeKind.Method, label, Location: new SourceLocation("A.cs", 1));

    private static FlowGraph Graph(IEnumerable<FlowNode> nodes, IEnumerable<FlowEdge> edges) =>
        new(RastroSchema.Version, "Job.Run", "m:job", nodes.ToList(), edges.ToList());

    private static FlowEdge Edge(string from, string to, ChangeKind? change = null) => new(from, to, 0, EdgeConfidence.Extracted, Change: change);

    private static readonly FlowNode Job = Method("m:job", "Job.Run");
    private static readonly FlowNode Send = Method("m:send", "Sender.Send");
    private static readonly FlowNode Log = Method("m:log", "Logger.Write");

    private static readonly FlowGraph Original = Graph([Job, Send, Log], [Edge("m:job", "m:send"), Edge("m:job", "m:log")]);

    private static readonly ValidationCheck Ok = new("build", CheckStatus.Passed);
    private static readonly ValidationCheck Skipped = new("build", CheckStatus.Skipped);
    private static readonly ValidationCheck Broken = new("build", CheckStatus.Failed);

    [Fact] // CORE-VAL-001
    public void Compare_PassoNovoChamadoNoCodigoNovo_CriterioCumpridoPeloNome()
    {
        var draft = Graph([Job, Send, Log, new FlowNode("new:audit", NodeKind.Method, "Audit.Record", Change: ChangeKind.Added, Intent: "registrar")],
            [.. Original.Edges, Edge("m:job", "new:audit", ChangeKind.Added)]);
        var after = Graph([Job, Send, Log, Method("m:audit", "Audit.Record")], [.. Original.Edges, Edge("m:job", "m:audit")]);

        var (expectations, unexpected) = ChangeValidator.Compare(Original, draft, after);

        Assert.Equal(CheckStatus.Passed, Assert.Single(expectations).Status);
        Assert.Empty(unexpected);
    }

    [Fact] // CORE-VAL-002
    public void Compare_PassoNovoAusente_CriterioFalhaComMotivo()
    {
        var draft = Graph([Job, Send, Log, new FlowNode("new:audit", NodeKind.Method, "Audit.Record", Change: ChangeKind.Added, Intent: "registrar")],
            [.. Original.Edges, Edge("m:job", "new:audit", ChangeKind.Added)]);

        var (expectations, _) = ChangeValidator.Compare(Original, draft, Original);

        var check = Assert.Single(expectations);
        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.Contains("Audit.Record não aparece", check.Detail);
    }

    [Fact] // CORE-VAL-003
    public void Compare_ChamadaRemovidaQueContinua_Falha_EQueSumiu_Passa()
    {
        var draft = Graph([Job, Send, Log], [Edge("m:job", "m:send"), Edge("m:job", "m:log", ChangeKind.Removed)]);
        var after = Graph([Job, Send], [Edge("m:job", "m:send")]);

        Assert.Equal(CheckStatus.Failed, Assert.Single(ChangeValidator.Compare(Original, draft, Original).Expectations).Status);
        Assert.Equal(CheckStatus.Passed, Assert.Single(ChangeValidator.Compare(Original, draft, after).Expectations).Status);
    }

    [Fact] // CORE-VAL-004
    public void Compare_ChamadaNovaForaDoPedido_ApareceComoInesperada()
    {
        var draft = Original with { Nodes = [Job, Send with { Change = ChangeKind.Modified, Intent = "enviar só em dia útil" }, Log] };
        var after = Graph([Job, Send, Log, Method("m:cache", "Cache.Clear")], [.. Original.Edges, Edge("m:send", "m:cache")]);

        var (expectations, unexpected) = ChangeValidator.Compare(Original, draft, after);

        Assert.Equal(CheckStatus.Passed, Assert.Single(expectations).Status);
        Assert.Equal("nova chamada fora do pedido: Sender.Send → Cache.Clear", Assert.Single(unexpected));
    }

    [Fact] // CORE-VAL-005
    public void Judge_BuildQuebrado_Reprova_MesmoComCriteriosOk()
    {
        IReadOnlyList<ValidationCheck> all = [new("a", CheckStatus.Passed)];

        Assert.Equal((ValidationVerdict.Rejected, 100), ChangeValidator.Judge(all, [], Broken, Ok));
        Assert.Equal((ValidationVerdict.Approved, 100), ChangeValidator.Judge(all, [], Ok, Ok));
        // Build sem comando não aprova nem reprova sozinho.
        Assert.Equal((ValidationVerdict.Approved, 100), ChangeValidator.Judge(all, [], Skipped, Skipped));
    }

    [Fact] // CORE-VAL-006
    public void Judge_ParteDosCriteriosOuInesperado_Parcial_NenhumCriterio_Reprova()
    {
        IReadOnlyList<ValidationCheck> half = [new("a", CheckStatus.Passed), new("b", CheckStatus.Failed)];
        IReadOnlyList<ValidationCheck> none = [new("a", CheckStatus.Failed)];
        IReadOnlyList<ValidationCheck> all = [new("a", CheckStatus.Passed)];

        Assert.Equal((ValidationVerdict.Partial, 50), ChangeValidator.Judge(half, [], Ok, Ok));
        Assert.Equal((ValidationVerdict.Partial, 100), ChangeValidator.Judge(all, ["x"], Ok, Ok));
        Assert.Equal((ValidationVerdict.Rejected, 0), ChangeValidator.Judge(none, [], Ok, Ok));
    }

    [Fact] // CORE-VAL-007
    public void CarryRules_LevaAsRegrasMenosDoPassoAlterado()
    {
        var withRules = Original with
        {
            Nodes = [Job with { Rule = new NodeRule("roda de hora em hora", ProposalOrigin.Agent) }, Send with { Rule = new NodeRule("envia sempre", ProposalOrigin.Agent) }, Log]
        };
        var draft = Original with { Nodes = [Job, Send with { Change = ChangeKind.Modified, Intent = "enviar só em dia útil" }, Log] };

        var carried = ChangeValidator.CarryRules(withRules, draft, Original);

        Assert.Equal("roda de hora em hora", carried.Nodes.Single(n => n.Id == "m:job").Rule?.Text);
        Assert.Null(carried.Nodes.Single(n => n.Id == "m:send").Rule);
    }

    [Fact] // CORE-VAL-010
    public void Compare_PassoNovoComTipos_CasaPelaAssinaturaDaSobrecarga()
    {
        var draft = Graph([Job, Send, Log, new FlowNode("new:audit", NodeKind.Method, "Audit.Record(string, int)", Change: ChangeKind.Added, Intent: "registrar")],
            [.. Original.Edges, Edge("m:job", "new:audit", ChangeKind.Added)]);
        var wrongOverload = new FlowNode("m:audit1", NodeKind.Method, "Audit.Record", Symbol: "App.Audit.Record(System.String)");
        var rightOverload = new FlowNode("m:audit2", NodeKind.Method, "Audit.Record", Symbol: "App.Audit.Record(string, System.Int32)");
        var onlyWrong = Graph([Job, Send, Log, wrongOverload], [.. Original.Edges, Edge("m:job", "m:audit1")]);
        var withRight = Graph([Job, Send, Log, wrongOverload, rightOverload], [.. Original.Edges, Edge("m:job", "m:audit2")]);

        Assert.Equal(CheckStatus.Failed, Assert.Single(ChangeValidator.Compare(Original, draft, onlyWrong).Expectations).Status);
        Assert.Equal(CheckStatus.Passed, Assert.Single(ChangeValidator.Compare(Original, draft, withRight).Expectations).Status);
    }

    [Fact] // CORE-VAL-011
    public void Compare_DoisPassosComOMesmoNome_VenceOChamadoPelaOrigem()
    {
        var draft = Graph([Job, Send, Log, new FlowNode("new:save", NodeKind.Method, "Repo.Save", Change: ChangeKind.Added, Intent: "gravar")],
            [.. Original.Edges, Edge("m:send", "new:save", ChangeKind.Added)]);
        var otherRepo = new FlowNode("m:repoA", NodeKind.Method, "Repo.Save", Symbol: "A.Repo.Save()");
        var sendRepo = new FlowNode("m:repoB", NodeKind.Method, "Repo.Save", Symbol: "B.Repo.Save()");
        var after = Graph([Job, Send, Log, otherRepo, sendRepo], [.. Original.Edges, Edge("m:log", "m:repoA"), Edge("m:send", "m:repoB")]);

        Assert.Equal(CheckStatus.Passed, Assert.Single(ChangeValidator.Compare(Original, draft, after).Expectations).Status);
    }

    [Theory] // CORE-VAL-012
    [InlineData("Audit.Record(string mensagem, List<Alert> itens)", "App.Audit.Record(System.String, System.Collections.Generic.List<App.Alert>)", true)]
    [InlineData("Audit.Record()", "App.Audit.Record()", true)]
    [InlineData("Audit.Record(int)", "App.Audit.Record(long)", false)]
    public void Assinatura_TiposCurtosAliasENomesDeParametro(string draftLabel, string symbol, bool matches)
    {
        var node = new FlowNode("m:a", NodeKind.Method, "Audit.Record", Symbol: symbol);

        Assert.Equal(matches, StepSignature.Matches(draftLabel, node));
    }

    [Fact] // CORE-VAL-008
    public void Documento_TemRegrasDiagramaEHistorico()
    {
        var draft = Original with { Nodes = [Job, Send with { Change = ChangeKind.Modified, Intent = "enviar só em dia útil" }, Log] };
        var flow = Original with { Nodes = [Job with { Rule = new NodeRule("roda de hora em hora", ProposalOrigin.Agent) }, Send, Log] };
        var (expectations, unexpected) = ChangeValidator.Compare(Original, draft, flow);
        var result = new ChangeValidationResult(expectations, unexpected, 0, 0, Ok, Skipped, ValidationVerdict.Approved, 100);

        var doc = FlowDocumentBuilder.Build(new PromptContext("repo", "App.slnx", "Job.Run", "abc"), flow, Original, draft, result,
            "0123456789", new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));

        Assert.Contains("# Fluxo `Job.Run`", doc);
        Assert.Contains("commit `0123456`", doc);
        Assert.Contains("1. **Job.Run** (`A.cs:1`): roda de hora em hora", doc);
        Assert.Contains("```mermaid\nflowchart LR", doc.ReplaceLineEndings("\n"));
        Assert.Contains("n1 --> n2", doc);
        Assert.Contains("Regra mudou em `Sender.Send`: enviar só em dia útil", doc);
        Assert.Contains("Validação: **aprovada**, 100%", doc);
        Assert.Contains("testes: não configurado", doc);
    }

    [Fact] // CORE-VAL-009
    public void Documento_ComAnotacoes_ListaNaOrdemDoFluxo()
    {
        var result = new ChangeValidationResult([], [], 0, 0, Ok, Skipped, ValidationVerdict.Approved, 100);
        var notes = new Dictionary<string, string> { ["m:send"] = "chamado também pelo webhook", ["m:fora"] = "não está no fluxo" };

        var doc = FlowDocumentBuilder.Build(new PromptContext("repo", "App.slnx", "Job.Run", "abc"), Original, Original, Original, result,
            null, new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), notes);

        Assert.Contains("## Anotações", doc);
        Assert.Contains("- **Sender.Send**: chamado também pelo webhook", doc);
        Assert.DoesNotContain("não está no fluxo", doc);
    }
}
