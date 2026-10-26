using Rastro.Core;
using Rastro.Core.Graph;

namespace Rastro.Tests.Core;

// Cenários CORE-DRAFT-NNN: o rascunho só marca o que é do código e acrescenta o que é novo.
public class DraftValidatorTests
{
    private static FlowGraph Original() => new(
        RastroSchema.Version, "Job.Run", "entry",
        [
            new FlowNode("entry", NodeKind.Entry, "Job.Run", Location: new SourceLocation("Job.cs", 3)),
            new FlowNode("a", NodeKind.Method, "Job.A", Location: new SourceLocation("Job.cs", 10)),
            new FlowNode("if", NodeKind.Decision, "if (x)", Location: new SourceLocation("Job.cs", 12))
        ],
        [
            new FlowEdge("entry", "a", 1, EdgeConfidence.Extracted),
            new FlowEdge("a", "if", 1, EdgeConfidence.Extracted)
        ]);

    // Rascunho válido: passo novo ligado depois de A, ligação A → if removida, decisão modificada.
    private static FlowGraph ValidDraft()
    {
        var original = Original();
        return original with
        {
            Nodes =
            [
                original.Nodes[0],
                original.Nodes[1],
                original.Nodes[2] with { Change = ChangeKind.Modified, Intent = "Só seguir quando o alerta não estiver silenciado." },
                new FlowNode("new:1", NodeKind.Method, "Job.Validar", Change: ChangeKind.Added, Intent: "Validar o alerta antes de enviar.")
            ],
            Edges =
            [
                original.Edges[0],
                original.Edges[1] with { Change = ChangeKind.Removed },
                new FlowEdge("a", "new:1", 2, EdgeConfidence.Extracted, Change: ChangeKind.Added),
                new FlowEdge("new:1", "if", 1, EdgeConfidence.Extracted, Change: ChangeKind.Added)
            ]
        };
    }

    [Fact] // CORE-DRAFT-001
    public void RascunhoQueMarcaEAcrescenta_EValido()
    {
        Assert.Empty(DraftValidator.Validate(Original(), ValidDraft()));
        Assert.True(DraftValidator.HasChanges(ValidDraft()));
        Assert.False(DraftValidator.HasChanges(Original()));
    }

    [Fact] // CORE-DRAFT-002
    public void ApagarOuAlterarPassoDoCodigo_EInvalido()
    {
        var draft = ValidDraft();

        var deleted = draft with { Nodes = draft.Nodes.Where(n => n.Id != "a").ToList() };
        var renamed = draft with { Nodes = draft.Nodes.Select(n => n.Id == "a" ? n with { Label = "Outro" } : n).ToList() };

        Assert.Contains(DraftValidator.Validate(Original(), deleted), e => e.Contains("sumiu"));
        Assert.Contains(DraftValidator.Validate(Original(), renamed), e => e.Contains("foi alterado"));
    }

    [Fact] // CORE-DRAFT-003
    public void PassoNovoSemIntencaoOuIdForaDoPadrao_EInvalido()
    {
        var draft = ValidDraft();
        var noIntent = draft with { Nodes = draft.Nodes.Select(n => n.Id == "new:1" ? n with { Intent = " " } : n).ToList() };
        var badId = draft with
        {
            Nodes = draft.Nodes.Select(n => n.Id == "new:1" ? n with { Id = "m:inventado" } : n).ToList(),
            Edges = draft.Edges.Where(e => e.Change != ChangeKind.Added).ToList()
        };

        Assert.Contains(DraftValidator.Validate(Original(), noIntent), e => e.Contains("o que deve fazer"));
        Assert.Contains(DraftValidator.Validate(Original(), badId), e => e.Contains("new:"));
    }

    [Fact] // CORE-DRAFT-004
    public void LigacaoDoCodigoApagadaOuNovaParaPassoInexistente_EInvalido()
    {
        var draft = ValidDraft();
        var edgeGone = draft with { Edges = draft.Edges.Where(e => !(e.From == "entry" && e.To == "a")).ToList() };
        var dangling = draft with { Edges = [.. draft.Edges, new FlowEdge("a", "new:nao-existe", 3, EdgeConfidence.Extracted, Change: ChangeKind.Added)] };
        var modifiedWithoutIntent = draft with { Nodes = draft.Nodes.Select(n => n.Id == "if" ? n with { Intent = null } : n).ToList() };

        Assert.Contains(DraftValidator.Validate(Original(), edgeGone), e => e.Contains("ligação do código"));
        Assert.Contains(DraftValidator.Validate(Original(), dangling), e => e.Contains("não está no rascunho"));
        Assert.Contains(DraftValidator.Validate(Original(), modifiedWithoutIntent), e => e.Contains("sem dizer o que muda"));
    }
}
