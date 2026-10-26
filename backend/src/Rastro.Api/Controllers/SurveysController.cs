using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rastro.Api.Analysis;
using Rastro.Core.Architecture;
using Rastro.Core.Graph;
using Rastro.Core.Prompts;
using Rastro.Core.Surveys;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Proposals;
using Rastro.Infrastructure.Repositories;
using Rastro.Infrastructure;

namespace Rastro.Api.Controllers;

[ApiController]
[Route("api/surveys")]
public class SurveysController(
    RastroDbContext db,
    RepositoryCatalog catalog,
    IGitInfo git,
    SurveyQueue queue,
    IOptions<RastroOptions> options,
    TimeProvider clock,
    ILogger<SurveysController> logger,
    Rastro.Infrastructure.Architecture.DeploymentReader deployments) : ControllerBase
{
    private const int ListLimit = 100;

    [HttpPost]
    public async Task<ActionResult<SurveySummary>> Create(CreateSurveyRequest request, CancellationToken ct)
    {
        var analysis = options.Value.Analysis;
        var entryPoint = request.EntryPoint?.Trim();
        if (string.IsNullOrEmpty(entryPoint) || entryPoint.Length > 500)
            return Error(ErrorCodes.EntryPointRequired, "Informe o ponto de entrada (Tipo.Metodo).");

        var maxDepth = request.MaxDepth ?? analysis.DefaultMaxDepth;
        if (maxDepth < 1 || maxDepth > analysis.MaxDepthLimit)
            return Error(ErrorCodes.DepthOutOfRange, $"A profundidade vai de 1 a {analysis.MaxDepthLimit}.");

        var resolved = catalog.Resolve(request.Repository ?? string.Empty, request.SolutionPath ?? string.Empty);
        if (resolved.Target is not { } target)
        {
            return resolved.Error switch
            {
                CatalogError.RepositoryNotAllowed => Error(ErrorCodes.RepositoryNotAllowed, "Repositório fora da lista de permitidos."),
                CatalogError.SolutionNotFound => Error(ErrorCodes.SolutionNotFound, "Solution não encontrada no repositório."),
                _ => Error(ErrorCodes.SolutionPathInvalid, "Caminho da solution inválido: use um .sln, .slnx ou .csproj relativo à raiz do repositório.")
            };
        }

        var head = await git.HeadAsync(target.RepositoryRoot, ct);
        var survey = Survey.Create(target.RepositoryName, target.SolutionRelativePath, entryPoint, maxDepth, head?.Commit, head?.Branch, clock.GetUtcNow());
        db.Surveys.Add(survey);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(survey.Id, ct);

        return AcceptedAtAction(nameof(Get), new { id = survey.Id }, SurveySummary.From(survey));
    }

    [HttpGet]
    public async Task<IReadOnlyList<SurveySummary>> List(CancellationToken ct)
    {
        var surveys = await db.Surveys.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .Take(ListLimit)
            .ToListAsync(ct);
        return surveys.Select(SurveySummary.From).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SurveyDetail>> Get(Guid id, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey is null) return NotFound();

        return new SurveyDetail(SurveySummary.From(survey), survey.ReadGraph());
    }

    // Excluir levantamento (Fase 13): some com rascunho, propostas, validações e pessoas do C4
    // (cascata). As anotações ficam: são do repositório e do passo, não do levantamento.
    // Em análise ou com validação rodando, recusa: o worker ainda escreve nele.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey is null) return NotFound();
        if (survey.Status is SurveyStatus.Pending or SurveyStatus.Running)
            return Error(ErrorCodes.SurveyRunning, "O levantamento ainda está em análise. Espere terminar para excluir.");
        var validating = await db.SurveyValidations.AnyAsync(
            v => (v.SurveyId == id || v.NewSurveyId == id) && (v.Status == Rastro.Core.Validation.ValidationStatus.Pending || v.Status == Rastro.Core.Validation.ValidationStatus.Running), ct);
        if (validating) return Error(ErrorCodes.ValidationRunning, "Há uma validação rodando com este levantamento. Espere terminar para excluir.");

        db.Surveys.Remove(survey);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Levantamento {SurveyId} excluído.", id);
        return NoContent();
    }

    // O código mudou desde o levantamento? Compara o commit salvo com o HEAD atual.
    [HttpGet("{id:guid}/stale")]
    public async Task<ActionResult<SurveyStaleness>> Stale(Guid id, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey is null) return NotFound();

        var root = catalog.RootOf(survey.Repository);
        var head = root is null ? null : await git.HeadAsync(root, ct);
        var stale = survey.CommitHash is not null && head is not null && head.Commit != survey.CommitHash;

        return new SurveyStaleness(stale, survey.CommitHash, head?.Commit);
    }

    // Trecho do código em volta de uma linha (o arquivo atual do disco, que pode ter mudado
    // desde o levantamento: a tela avisa quando o levantamento está desatualizado).
    [HttpGet("{id:guid}/source")]
    public async Task<ActionResult<SourceSnippet>> Source(Guid id, [FromQuery] string? file, [FromQuery] int line, [FromQuery] int context = 8, CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey is null) return NotFound();

        var path = catalog.SourceFile(survey.Repository, file ?? string.Empty);
        if (path is null) return Error(ErrorCodes.SourcePathInvalid, "Arquivo inválido: use um .cs relativo à raiz do repositório.");
        if (!System.IO.File.Exists(path)) return NotFound();

        context = Math.Clamp(context, 0, 40);
        var lines = await System.IO.File.ReadAllLinesAsync(path, ct);
        var target = Math.Clamp(line, 1, Math.Max(lines.Length, 1));
        var start = Math.Max(1, target - context);
        var end = Math.Min(lines.Length, target + context);

        return new SourceSnippet(file!.Replace('\\', '/'), start, target, lines[(start - 1)..end]);
    }

    // Você decide sobre uma aresta proposta por agente ou provedor: confirmar ou descartar.
    [HttpPost("{id:guid}/edges/decision")]
    public async Task<ActionResult<SurveyDetail>> DecideEdge(Guid id, EdgeDecisionRequest request, [FromServices] ProposalService proposals, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.To))
            return Error(ErrorCodes.EdgeNotProposed, "Informe a aresta (origem e destino).");

        if (!await db.Surveys.AnyAsync(s => s.Id == id, ct)) return NotFound();

        var outcome = await proposals.DecideEdgeAsync(id, request.From, request.To, request.Confirm, ct);
        if (!outcome.Accepted) return Error(ErrorCodes.EdgeNotProposed, outcome.Message);

        return await Get(id, ct);
    }

    // Rascunho de edição (Fase 7): o fluxo como o usuário quer que fique, validado contra o extraído.
    [HttpGet("{id:guid}/draft")]
    public async Task<ActionResult<FlowGraph>> GetDraft(Guid id, CancellationToken ct)
    {
        var draft = await db.SurveyDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.SurveyId == id, ct);
        return draft is null ? NotFound() : draft.ReadGraph();
    }

    [HttpPut("{id:guid}/draft")]
    public async Task<ActionResult<FlowGraph>> SaveDraft(Guid id, FlowGraph draft, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey is null) return NotFound();
        if (survey.ReadGraph() is not { } original)
            return Error(ErrorCodes.DraftInvalid, "O levantamento ainda não tem grafo para editar.");

        var errors = DraftValidator.Validate(original, draft);
        if (errors.Count > 0)
        {
            var problem = new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Rascunho inválido." };
            problem.Extensions["code"] = ErrorCodes.DraftInvalid;
            problem.Extensions["errors"] = errors;
            return BadRequest(problem);
        }

        var stored = await db.SurveyDrafts.FirstOrDefaultAsync(d => d.SurveyId == id, ct);
        if (stored is null) db.SurveyDrafts.Add(SurveyDraft.Create(id, draft, clock.GetUtcNow()));
        else stored.Update(draft, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return draft;
    }

    [HttpDelete("{id:guid}/draft")]
    public async Task<IActionResult> DeleteDraft(Guid id, CancellationToken ct)
    {
        var stored = await db.SurveyDrafts.FirstOrDefaultAsync(d => d.SurveyId == id, ct);
        if (stored is null) return NotFound();
        db.SurveyDrafts.Remove(stored);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Prompt da mudança (Fase 8), montado do rascunho salvo. Sem IA: determinístico.
    [HttpGet("{id:guid}/prompt")]
    public async Task<ActionResult<ChangePrompt>> Prompt(Guid id, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        var draft = await db.SurveyDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.SurveyId == id, ct);
        if (survey?.ReadGraph() is not { } original || draft is null) return NotFound();

        var edited = draft.ReadGraph();
        var context = new PromptContext(survey.Repository, survey.SolutionPath, survey.EntryPoint, survey.CommitHash);
        return new ChangePrompt(ChangePromptBuilder.Build(context, original, edited), DraftDiff.Compute(original, edited).Count);
    }

    // Prompt de refatoração de um achado de qualidade (Fase 10), pela posição na lista do grafo.
    [HttpGet("{id:guid}/findings/{index:int}/prompt")]
    public async Task<ActionResult<RefactorPrompt>> RefactorPrompt(Guid id, int index, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey?.ReadGraph() is not { } graph) return NotFound();
        if (graph.Findings is not { } findings || index < 0 || index >= findings.Count) return NotFound();

        var context = new PromptContext(survey.Repository, survey.SolutionPath, survey.EntryPoint, survey.CommitHash);
        return new RefactorPrompt(RefactorPromptBuilder.Build(context, graph, findings[index]));
    }

    // Diagramas C4 em Structurizr DSL (Fase 11), com as pessoas do C1 salvas para o levantamento.
    [HttpGet("{id:guid}/c4")]
    public async Task<ActionResult<C4Export>> C4(Guid id, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey?.ReadGraph() is not { } graph) return NotFound();

        var people = (await db.SurveyArchitectures.AsNoTracking().FirstOrDefaultAsync(a => a.SurveyId == id, ct))?.ReadPeople() ?? [];
        return new C4Export(BuildC4(survey, graph, people), people);
    }

    [HttpPut("{id:guid}/c4/people")]
    public async Task<ActionResult<C4Export>> SaveC4People(Guid id, List<C4Person> people, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey?.ReadGraph() is not { } graph) return NotFound();

        var clean = people.Select(p => new C4Person(p.Name?.Trim() ?? "", string.IsNullOrWhiteSpace(p.Description) ? null : p.Description.Trim(), p.FromAi == true ? true : null)).ToList();
        var errors = SurveyArchitecture.Validate(clean);
        if (errors.Count > 0)
        {
            var problem = new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Pessoas inválidas." };
            problem.Extensions["code"] = ErrorCodes.C4PeopleInvalid;
            problem.Extensions["errors"] = errors;
            return BadRequest(problem);
        }

        var stored = await db.SurveyArchitectures.FirstOrDefaultAsync(a => a.SurveyId == id, ct);
        if (stored is null) db.SurveyArchitectures.Add(SurveyArchitecture.Create(id, clean, clock.GetUtcNow()));
        else stored.Update(clean, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return new C4Export(BuildC4(survey, graph, clean), clean);
    }

    // Anotações dos passos deste fluxo (Fase 12): ficam por repositório e passo.
    [HttpGet("{id:guid}/notes")]
    public async Task<ActionResult<IReadOnlyList<NodeNoteView>>> Notes(Guid id, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey?.ReadGraph() is not { } graph) return NotFound();

        var nodeIds = graph.Nodes.Select(n => n.Id).ToHashSet();
        var notes = await db.NodeNotes.AsNoTracking().Where(n => n.Repository == survey.Repository).ToListAsync(ct);
        return notes.Where(n => nodeIds.Contains(n.NodeId)).Select(n => new NodeNoteView(n.NodeId, n.Text, n.UpdatedAt)).ToList();
    }

    [HttpPut("{id:guid}/notes")]
    public async Task<IActionResult> SaveNote(Guid id, NodeNoteInput input, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey?.ReadGraph() is not { } graph) return NotFound();

        // O passo precisa existir neste fluxo: a nota não vira depósito de texto solto.
        if (string.IsNullOrWhiteSpace(input.NodeId) || graph.Nodes.All(n => n.Id != input.NodeId))
            return Error(ErrorCodes.NoteInvalid, "O passo da anotação não está neste fluxo.");
        var text = input.Text?.Trim() ?? "";
        if (NodeNote.Validate(text) is { } problem) return Error(ErrorCodes.NoteInvalid, problem);

        var stored = await db.NodeNotes.FirstOrDefaultAsync(n => n.Repository == survey.Repository && n.NodeId == input.NodeId, ct);
        if (text.Length == 0)
        {
            if (stored is not null) db.NodeNotes.Remove(stored);
            await db.SaveChangesAsync(ct);
            return NoContent();
        }

        if (stored is null)
        {
            stored = NodeNote.Create(survey.Repository, input.NodeId, text, clock.GetUtcNow());
            db.NodeNotes.Add(stored);
        }
        else
        {
            stored.Update(text, clock.GetUtcNow());
        }
        await db.SaveChangesAsync(ct);
        return Ok(new NodeNoteView(stored.NodeId, stored.Text, stored.UpdatedAt));
    }

    // Regra cruzada entre fluxos (Fase 13): compara com o levantamento pronto mais recente de cada
    // outro ponto de entrada do mesmo repositório.
    [HttpGet("{id:guid}/cross-flow")]
    public async Task<ActionResult<IReadOnlyList<CrossFlowView>>> CrossFlow(Guid id, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey?.ReadGraph() is not { } graph) return NotFound();

        var siblings = await db.Surveys.AsNoTracking()
            .Where(s => s.Repository == survey.Repository && s.EntryPoint != survey.EntryPoint && s.Status == SurveyStatus.Completed)
            .OrderByDescending(s => s.CreatedAt)
            .Take(ListLimit)
            .ToListAsync(ct);
        var others = siblings
            .GroupBy(s => s.EntryPoint)
            .Select(g => g.First())
            .Select(s => s.ReadGraph() is { } g ? new FlowSample(s.Id, s.EntryPoint, g) : null)
            .OfType<FlowSample>()
            .ToList();

        var labels = graph.Nodes.ToDictionary(n => n.Id, n => n.Label);
        return CrossFlowInspector.Inspect(new FlowSample(survey.Id, survey.EntryPoint, graph), others)
            .Select(f => new CrossFlowView(f.NodeId, labels.GetValueOrDefault(f.NodeId, f.NodeId), f.Entity, f.Guard, f.Flows))
            .ToList();
    }

    [HttpGet("{id:guid}/c4/workspace.dsl")]
    public async Task<IActionResult> C4File(Guid id, CancellationToken ct)
    {
        var result = await C4(id, ct);
        if (result.Value is not { } export) return NotFound();
        return File(System.Text.Encoding.UTF8.GetBytes(export.Dsl), "text/plain; charset=utf-8", "workspace.dsl");
    }

    private string BuildC4(Survey survey, FlowGraph graph, IReadOnlyList<C4Person> people)
    {
        var root = catalog.Resolve(survey.Repository, survey.SolutionPath).Target?.RepositoryRoot;
        var deployment = root is null ? null : deployments.Read(root);
        return C4WorkspaceBuilder.Build(new PromptContext(survey.Repository, survey.SolutionPath, survey.EntryPoint, survey.CommitHash), graph, people, deployment);
    }

    private BadRequestObjectResult Error(string code, string message)
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = message };
        problem.Extensions["code"] = code;
        return BadRequest(problem);
    }
}
