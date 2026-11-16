using Rastro.Core;
using Rastro.Core.Failures;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Core.Validation;

namespace Rastro.Tests.Core;

// Cenários CORE-FAIL-NNN: a falha viaja como código e parâmetros (Fase 49); o texto em português fica ao lado.
public class FailureCodesTests
{
    private static readonly string[] Many = ["a", "b", "c", "d", "e", "f"];
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact] // CORE-FAIL-001
    public void Texto_TodoCodigoDeFalha_TemTextoEFormataComOsParametros()
    {
        // Os códigos aninhados levam o código de uma falha da análise no primeiro parâmetro.
        string[] nested = [((int)SurveyFailure.RestoreFailed).ToString(), "x"];

        foreach (var code in Enum.GetValues<SurveyFailure>()) Assert.False(string.IsNullOrWhiteSpace(FailureText.Of(code, Many)));
        foreach (var code in Enum.GetValues<ValidationFailure>())
            Assert.False(string.IsNullOrWhiteSpace(FailureText.Of(code, code == ValidationFailure.Analysis ? nested : Many)));
        foreach (var code in Enum.GetValues<ProposalRefusal>())
        {
            var nests = code is ProposalRefusal.AnalysisFailed or ProposalRefusal.ProofTargetInvalid;
            Assert.False(string.IsNullOrWhiteSpace(FailureText.Of(code, nests ? nested : Many)));
        }
        foreach (var code in Enum.GetValues<DraftError>()) Assert.False(string.IsNullOrWhiteSpace(FailureText.Of(code, Many)));
    }

    [Fact] // CORE-FAIL-002
    public void Enums_TodoMembro_TemValorExplicitoEEstavel()
    {
        Assert.Equal(0, (int)SurveyFailure.Unexpected);
        Assert.Equal(13, (int)SurveyFailure.RestoreFailed);
        Assert.Equal(0, (int)ValidationFailure.Unexpected);
        Assert.Equal(6, (int)ValidationFailure.Analysis);
        Assert.Equal(21, (int)ProposalRefusal.RouteMismatch);
        Assert.Equal(15, (int)DraftError.ForeignSystem);
    }

    [Fact] // CORE-FAIL-003
    public void Fail_FalhaConhecida_GravaCodigoParametrosEOTextoEmPortugues()
    {
        var survey = Survey.Create("loja", "Loja.slnx", "Job.Run", 5, null, null, Now);

        survey.Fail(SurveyFailure.EntryNotFound, ["Job.Run", "Run"], Now);

        Assert.Equal(SurveyStatus.Failed, survey.Status);
        Assert.Equal(SurveyFailure.EntryNotFound, survey.Failure);
        Assert.Equal(["Job.Run", "Run"], survey.ReadFailureArgs());
        Assert.Contains("nenhum método chamado Run", survey.Error);
    }

    [Fact] // CORE-FAIL-004
    public void Fail_FalhaInesperada_GravaOCodigoGenericoSemDetalheTecnico()
    {
        var validation = SurveyValidation.Create(Guid.NewGuid(), Now);

        validation.Fail(ValidationFailure.Unexpected, null, Now);

        Assert.Equal(ValidationFailure.Unexpected, validation.Failure);
        Assert.Empty(validation.ReadFailureArgs());
        Assert.Equal("Falha inesperada na validação. Detalhes no log.", validation.Error);
    }

    [Fact] // CORE-FAIL-005
    public void Start_LevantamentoQueFalhouERoda_LimpaOCodigoDaFalhaAnterior()
    {
        var survey = Survey.Create("loja", "Loja.slnx", "Job.Run", 5, null, null, Now);
        survey.Fail(SurveyFailure.Unexpected, null, Now);

        survey.Start(Now);

        Assert.Null(survey.Failure);
        Assert.Null(survey.Error);
        Assert.Empty(survey.ReadFailureArgs());
    }

    [Fact] // CORE-FAIL-006
    public void Reject_ComCodigo_GravaOMotivoEmCodigoENoTexto()
    {
        var proposal = SurveyProposal.ForEdge(Guid.NewGuid(), ProposalOrigin.Agent, "n1", "A.B", "A.cs", 3, "porque sim", Now);

        proposal.Reject("texto", ProposalRefusal.DestinationUnavailable, ["loja"]);

        Assert.Equal(ProposalStatus.Rejected, proposal.Status);
        Assert.Equal(ProposalRefusal.DestinationUnavailable, proposal.Refusal);
        Assert.Contains("loja", proposal.RefusalArgsJson);
        Assert.Equal("texto", proposal.RejectionReason);
    }

    [Fact] // CORE-FAIL-007
    public void ParseArgs_JsonInvalido_VoltaListaVazia()
    {
        Assert.Empty(FailureText.ParseArgs("{isso não é lista"));
        Assert.Empty(FailureText.ParseArgs(null));
    }

    [Fact] // CORE-FAIL-008
    public void ValidateCoded_PassoDoCodigoSumiu_TrazCodigoEParametrosEOMesmoTextoDeValidate()
    {
        var original = new FlowGraph(RastroSchema.Version, "Job.Run", "entry",
            [new FlowNode("entry", NodeKind.Entry, "Job.Run"), new FlowNode("a", NodeKind.Method, "Job.A")],
            [new FlowEdge("entry", "a", 1, EdgeConfidence.Extracted)]);
        var broken = original with { Nodes = [original.Nodes[0]], Edges = [] };

        var coded = DraftValidator.ValidateCoded(original, broken);

        Assert.Contains(coded, e => e.Code == DraftError.CodeStepMissing && e.Args.SequenceEqual(["Job.A"]));
        Assert.Contains(coded, e => e.Code == DraftError.EdgeMissing && e.Args.SequenceEqual(["Job.Run", "Job.A"]));
        Assert.Equal(coded.Select(e => e.Text), DraftValidator.Validate(original, broken));
    }
}
