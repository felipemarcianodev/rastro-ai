using Microsoft.AspNetCore.Mvc;
using Rastro.Core.Business;
using Rastro.Core.Localization;
using Rastro.Infrastructure.Business;

namespace Rastro.Api.Controllers;

// Corpos do quadro de fluxos (Fase 60), em camelCase no JSON.
public record CreateBoardRequest(string? Name, IReadOnlyList<Guid>? SurveyIds, IReadOnlyList<string?>? Titles = null);

public record BoardLaneRequest(Guid SurveyId, int Order, string? Title);

public record UpdateBoardRequest(string? Name, string? Glossary, IReadOnlyList<BoardLaneRequest>? Lanes);

public record AddBoardLaneRequest(Guid SurveyId, string? Title = null);

public record ReplaceBoardLaneRequest(Guid NewSurveyId);

// Resposta da troca de levantamento da faixa: o quadro já com a faixa nova e os passos do antigo que não continuam.
public record ReplaceBoardLaneResponse(BoardDetail Board, IReadOnlyList<RemovedStep> RemovedSteps);

// Quadro de fluxos (Fase 60): vários levantamentos lado a lado. Tudo no banco local. A posição dos balões é por
// quadro; texto e tipo dos passos são do levantamento (Fase 59) e se editam pelas rotas dele.
[ApiController]
[Route("api/boards")]
public class BoardsController(BoardService boards, ILogger<BoardsController> logger) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<BoardSummary>> List(CancellationToken ct) => boards.ListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<BoardDetail>> Create(CreateBoardRequest request, CancellationToken ct)
    {
        var created = await boards.CreateAsync(request.Name, request.SurveyIds, request.Titles, ct);
        if (!created.Ok) return Fail(created.Error);
        var detail = await boards.GetAsync(created.Value, RequestLanguage.Current, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Value }, detail);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BoardDetail>> Get(Guid id, CancellationToken ct)
    {
        var detail = await boards.GetAsync(id, RequestLanguage.Current, ct);
        return detail is null ? Fail(Rastro.Infrastructure.Business.BoardError.NotFound) : detail;
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BoardDetail>> Update(Guid id, UpdateBoardRequest request, CancellationToken ct)
    {
        var lanes = request.Lanes?.Select(l => new BoardLaneInput(l.SurveyId, l.Order, l.Title)).ToList();
        var updated = await boards.UpdateAsync(id, request.Name, request.Glossary, lanes, ct);
        return updated.Ok ? await DetailAsync(id, ct) : Fail(updated.Error);
    }

    [HttpPost("{id:guid}/lanes")]
    public async Task<ActionResult<BoardDetail>> AddLane(Guid id, AddBoardLaneRequest request, CancellationToken ct)
    {
        var added = await boards.AddLaneAsync(id, request.SurveyId, request.Title, ct);
        return added.Ok ? await DetailAsync(id, ct) : Fail(added.Error);
    }

    [HttpPut("{id:guid}/lanes/{surveyId:guid}/replace")]
    public async Task<ActionResult<ReplaceBoardLaneResponse>> ReplaceLane(Guid id, Guid surveyId, ReplaceBoardLaneRequest request, CancellationToken ct)
    {
        var replaced = await boards.ReplaceLaneAsync(id, surveyId, request.NewSurveyId, RequestLanguage.Current, ct);
        if (!replaced.Ok) return Fail(replaced.Error);
        var detail = await boards.GetAsync(id, RequestLanguage.Current, ct);
        return new ReplaceBoardLaneResponse(detail!, replaced.Value!.RemovedSteps);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await boards.DeleteAsync(id, ct) ? NoContent() : Fail(Rastro.Infrastructure.Business.BoardError.NotFound);

    // Posição em lote, no escopo do quadro: cada passo e cada ligação levam a faixa (laneSurveyId). Mesmos limites da
    // visão de negócio de um levantamento (Fase 58), contados no lote inteiro.
    [HttpPut("{id:guid}/layout")]
    public async Task<ActionResult<BoardLayoutDto>> SaveLayout(Guid id, BoardLayoutDto layout,
        [FromServices] BusinessLayoutService layouts, CancellationToken ct)
    {
        var flat = new BusinessLayoutDto(
            (layout?.Steps ?? []).Select(s => new StepPositionDto(s.StepId, s.X, s.Y)).ToList(),
            (layout?.Edges ?? []).Select(e => new EdgePointsDto(e.From, e.To, e.Points)).ToList());
        if (layout is null || (layout.Steps ?? []).Any(s => s is null) || (layout.Edges ?? []).Any(e => e is null)
            || !BusinessLayoutLimits.IsValid(flat))
        {
            return await boards.ExistsAsync(id, ct)
                ? Fail(Rastro.Infrastructure.Business.BoardError.LayoutInvalid)
                : Fail(Rastro.Infrastructure.Business.BoardError.NotFound);
        }

        var saved = await boards.SaveLayoutAsync(id, layout, ct);
        return saved == Rastro.Infrastructure.Business.BoardError.None ? await layouts.GetBoardAsync(id, ct) : Fail(saved);
    }

    // "Reorganizar": a faixa (?lane=) ou, sem o parâmetro, o quadro inteiro.
    [HttpDelete("{id:guid}/layout")]
    public async Task<IActionResult> ResetLayout(Guid id, [FromQuery] Guid? lane, CancellationToken ct)
    {
        var reset = await boards.ResetLayoutAsync(id, lane, ct);
        return reset == Rastro.Infrastructure.Business.BoardError.None ? NoContent() : Fail(reset);
    }

    private async Task<ActionResult<BoardDetail>> DetailAsync(Guid id, CancellationToken ct) =>
        await boards.GetAsync(id, RequestLanguage.Current, ct) is { } detail
            ? detail
            : Fail(Rastro.Infrastructure.Business.BoardError.NotFound);

    // Erro estável: código para a tela traduzir e mensagem no idioma do pedido.
    private ObjectResult Fail(Rastro.Infrastructure.Business.BoardError error)
    {
        var language = UiLanguages.FromCulture(System.Globalization.CultureInfo.CurrentUICulture);
        var (status, code, key) = error switch
        {
            Rastro.Infrastructure.Business.BoardError.NameInvalid =>
                (StatusCodes.Status400BadRequest, ErrorCodes.BoardNameInvalid, "Board.NameInvalid"),
            Rastro.Infrastructure.Business.BoardError.GlossaryInvalid =>
                (StatusCodes.Status400BadRequest, ErrorCodes.BoardGlossaryInvalid, "Board.GlossaryInvalid"),
            Rastro.Infrastructure.Business.BoardError.LanesInvalid =>
                (StatusCodes.Status400BadRequest, ErrorCodes.BoardLanesInvalid, "Board.LanesInvalid"),
            Rastro.Infrastructure.Business.BoardError.LayoutInvalid =>
                (StatusCodes.Status400BadRequest, ErrorCodes.BusinessLayoutInvalid, "BusinessLayout.Invalid"),
            Rastro.Infrastructure.Business.BoardError.SurveyNotFound =>
                (StatusCodes.Status404NotFound, ErrorCodes.BoardSurveyNotFound, "Board.SurveyNotFound"),
            Rastro.Infrastructure.Business.BoardError.SurveyNotReady =>
                (StatusCodes.Status409Conflict, ErrorCodes.BoardSurveyNotReady, "Board.SurveyNotReady"),
            Rastro.Infrastructure.Business.BoardError.LaneExists =>
                (StatusCodes.Status409Conflict, ErrorCodes.BoardLaneExists, "Board.LaneExists"),
            Rastro.Infrastructure.Business.BoardError.LaneNotFound =>
                (StatusCodes.Status404NotFound, ErrorCodes.BoardLaneNotFound, "Board.LaneNotFound"),
            _ => (StatusCodes.Status404NotFound, ErrorCodes.BoardNotFound, "Board.NotFound")
        };
        logger.LogInformation("Pedido do quadro recusado com o código {Code}.", code);
        var problem = new ProblemDetails { Status = status, Title = Rastro.Api.Cli.ApiText.Get(key, language) };
        problem.Extensions["code"] = code;
        return StatusCode(status, problem);
    }
}
