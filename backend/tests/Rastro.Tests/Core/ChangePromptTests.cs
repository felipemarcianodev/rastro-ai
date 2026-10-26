using Rastro.Core;
using Rastro.Core.Graph;
using Rastro.Core.Prompts;

namespace Rastro.Tests.Core;

// Cenários CORE-PROMPT-NNN: diferença do rascunho, prompt determinístico e consultas de impacto.
public class ChangePromptTests
{
    private static readonly SourceLocation Job = new("src/Job.cs", 10);

    // entry -> a -> b ; entry -> c ; a -> if
    private static FlowGraph Original() => new(
        RastroSchema.Version, "Job.Run", "entry",
        [
            new FlowNode("entry", NodeKind.Entry, "Job.Run", "Ns.Job.Run()", new SourceLocation("src/Job.cs", 3)),
            new FlowNode("a", NodeKind.Method, "Job.A", "Ns.Job.A()", new SourceLocation("src/Job.cs", 10)),
            new FlowNode("b", NodeKind.Method, "Job.B", "Ns.Job.B()", new SourceLocation("src/Job.cs", 20)),
            new FlowNode("c", NodeKind.Method, "Job.C", "Ns.Job.C()", new SourceLocation("src/Job.cs", 30)),
            new FlowNode("if", NodeKind.Decision, "if (x)", Location: new SourceLocation("src/Job.cs", 12), Condition: "x",
                Rule: new NodeRule("Segue só com x.", ProposalOrigin.Agent))
        ],
        [
            new FlowEdge("entry", "a", 1, EdgeConfidence.Extracted),
            new FlowEdge("a", "b", 1, EdgeConfidence.Extracted),
            new FlowEdge("entry", "c", 2, EdgeConfidence.Extracted),
            new FlowEdge("a", "if", 2, EdgeConfidence.Extracted)
        ]);

    // Passo novo depois de C, decisão modificada, B movido de A para C.
    private static FlowGraph Draft()
    {
        var o = Original();
        return o with
        {
            Nodes =
            [
                .. o.Nodes.Select(n => n.Id == "if" ? n with { Change = ChangeKind.Modified, Intent = "Seguir também quando y." } : n),
                new FlowNode("new:audit", NodeKind.Method, "Audit.Registrar", Change: ChangeKind.Added, Intent: "Registrar a execução na auditoria.")
            ],
            Edges =
            [
                o.Edges[0],
                o.Edges[1] with { Change = ChangeKind.Removed },
                o.Edges[2],
                o.Edges[3],
                new FlowEdge("c", "b", 1, EdgeConfidence.Extracted, Change: ChangeKind.Added),
                new FlowEdge("c", "new:audit", 2, EdgeConfidence.Extracted, Change: ChangeKind.Added)
            ]
        };
    }

    [Fact] // CORE-PROMPT-001
    public void Diferenca_ReconhecePassoNovoModificadoEMovido()
    {
        var changes = DraftDiff.Compute(Original(), Draft());

        var added = Assert.Single(changes, c => c.Type == DraftChangeType.AddedStep);
        Assert.Equal("c", Assert.Single(added.CalledFrom).Id);
        Assert.Single(changes, c => c.Type == DraftChangeType.ModifiedStep && c.Node.Id == "if");
        var moved = Assert.Single(changes, c => c.Type == DraftChangeType.MovedStep);
        Assert.Equal(("b", "a", "c"), (moved.Node.Id, moved.PreviousCaller!.Id, moved.CalledFrom[0].Id));
        Assert.DoesNotContain(changes, c => c.Type is DraftChangeType.AddedLink or DraftChangeType.RemovedLink);
    }

    [Fact] // CORE-PROMPT-002
    public void Prompt_TemAncorasIntencaoOQueNaoMexerECriterios()
    {
        var prompt = ChangePromptBuilder.Build(new PromptContext("loja", "backend/loja.slnx", "Job.Run", "ce7cec50abc"), Original(), Draft());

        Assert.Contains("# Mudança no fluxo `Job.Run`", prompt);
        Assert.Contains("commit `ce7cec5`", prompt);
        Assert.Contains("Criar o passo `Audit.Registrar` (método)", prompt);
        Assert.Contains("Registrar a execução na auditoria.", prompt);
        Assert.Contains("chamado por `Job.C` (src/Job.cs:30)", prompt);
        Assert.Contains("Mudar `if (x)` em `src/Job.cs:12`", prompt);
        Assert.Contains("Como funciona hoje: Segue só com x.", prompt);
        Assert.Contains("Mover `Job.B` em `src/Job.cs:20`", prompt);
        Assert.Contains("Deixa de ser chamado por `Job.A` (src/Job.cs:10)", prompt);
        Assert.Contains("## O que não mexer", prompt);
        Assert.Contains("ter `Job.C` chamando `Audit.Registrar`", prompt);
        Assert.Contains("não ter `Job.A` chamando `Job.B`", prompt);
        Assert.Contains("check_change", prompt);
    }

    [Fact] // CORE-PROMPT-003
    public void Prompt_MesmoRascunho_MesmoTexto_ESemMudancaDizIsso()
    {
        var context = new PromptContext("r", "s.slnx", "Job.Run", null);

        Assert.Equal(ChangePromptBuilder.Build(context, Original(), Draft()), ChangePromptBuilder.Build(context, Original(), Draft()));
        Assert.Contains("Nenhuma mudança no rascunho.", ChangePromptBuilder.Build(context, Original(), Original()));
    }

    [Fact] // CORE-PROMPT-004
    public void Impacto_CaminhosAteONoEOQueRodaDepois()
    {
        var paths = GraphQueries.PathsTo(Original(), "b");
        var downstream = GraphQueries.Downstream(Original(), "a");

        Assert.Equal(["entry", "a", "b"], Assert.Single(paths).Select(n => n.Id));
        Assert.Equal(["b", "if"], downstream.Select(n => n.Id).Order());
        Assert.Empty(GraphQueries.PathsTo(Original(), "nao-existe"));
    }
}
