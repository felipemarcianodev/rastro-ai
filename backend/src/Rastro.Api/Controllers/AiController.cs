using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rastro.Ai;
using Rastro.Ai.Providers;
using Rastro.Ai.Rules;
using Rastro.Ai.Settings;
using Rastro.Ai.Suggestions;
using Rastro.Infrastructure;

namespace Rastro.Api.Controllers;

public record FillHoleRequest(string? NodeId);

public record FillHoleResponse(FillHoleResult Result, SurveyDetail Survey);

// nodeIds nulo = todos os nós com lógica ainda sem regra (até 36 por pedido).
public record DescribeRequest(IReadOnlyList<string>? NodeIds, bool Overwrite);

public record DescribeResponse(DescribeResult Result, SurveyDetail Survey);

// Modo provedor (ADR-001): configuração da IA no estilo feature flags, estado para a tela e o
// uso "completar buraco". A chave de API entra por aqui e nunca sai.
[ApiController]
public class AiController(AiSettingsService settings, AiGateway gateway) : ControllerBase
{
    [HttpGet("api/settings/ai")]
    public async Task<AiSettingsView> GetSettings(CancellationToken ct) => AiSettingsService.View(await settings.GetAsync(ct));

    [HttpPut("api/settings/ai")]
    public async Task<ActionResult<AiSettingsView>> UpdateSettings(AiSettingsUpdate update, CancellationToken ct)
    {
        try
        {
            return AiSettingsService.View(await settings.UpdateAsync(update, ct));
        }
        catch (ArgumentException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, AiErrorCodes.SettingsInvalid, ex.Message);
        }
    }

    [HttpGet("api/ai/status")]
    public Task<AiStatus> Status(CancellationToken ct) => gateway.StatusAsync(ct);

    [HttpPost("api/surveys/{id:guid}/ai/fill-hole")]
    public async Task<ActionResult<FillHoleResponse>> FillHole(Guid id, FillHoleRequest request, [FromServices] HoleFiller filler,
        [FromServices] RastroDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NodeId))
            return Problem(StatusCodes.Status400BadRequest, AiErrorCodes.NodeRequired, "Informe o nó não resolvido.");
        if (!await db.Surveys.AnyAsync(s => s.Id == id, ct)) return NotFound();

        return await RunAsync(async () =>
        {
            var result = await filler.FillAsync(id, request.NodeId, ct);
            return new FillHoleResponse(result, await DetailAsync(db, id, ct));
        });
    }

    [HttpPost("api/surveys/{id:guid}/ai/describe")]
    public async Task<ActionResult<DescribeResponse>> Describe(Guid id, DescribeRequest request, [FromServices] RuleWriter writer,
        [FromServices] RastroDbContext db, CancellationToken ct)
    {
        if (!await db.Surveys.AnyAsync(s => s.Id == id, ct)) return NotFound();

        return await RunAsync(async () =>
        {
            var result = await writer.DescribeAsync(id, request.NodeIds, request.Overwrite, ct);
            return new DescribeResponse(result, await DetailAsync(db, id, ct));
        });
    }

    // Sugestão de refactor da IA para um achado (Fase 13): texto para ler, marcado como da IA.
    [HttpPost("api/surveys/{id:guid}/findings/{index:int}/ai/suggestion")]
    public async Task<ActionResult<RefactorSuggestion>> Suggest(Guid id, int index, [FromServices] RefactorAdvisor advisor, CancellationToken ct)
    {
        try
        {
            return await RunAsync(() => advisor.SuggestAsync(id, index, ct));
        }
        catch (FindingNotFoundException)
        {
            return NotFound();
        }
    }

    // Pessoas do C1 sugeridas pela IA (Fase 13): entram só quando o usuário acrescenta e salva.
    [HttpPost("api/surveys/{id:guid}/c4/ai/people")]
    public async Task<ActionResult<PeopleSuggestion>> SuggestPeople(Guid id, [FromServices] ContextAdvisor advisor, CancellationToken ct)
    {
        try
        {
            return await RunAsync(() => advisor.SuggestPeopleAsync(id, ct));
        }
        catch (FindingNotFoundException)
        {
            return NotFound();
        }
    }

    private static async Task<SurveyDetail> DetailAsync(RastroDbContext db, Guid id, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().SingleAsync(s => s.Id == id, ct);
        return new SurveyDetail(SurveySummary.From(survey), survey.ReadGraph());
    }

    // Bloqueios e falhas do provedor viram códigos estáveis para a tela.
    private async Task<ActionResult<T>> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (AiBlockedException ex)
        {
            return ex.Reason == AiBlock.BudgetExceeded
                ? Problem(StatusCodes.Status429TooManyRequests, AiErrorCodes.BudgetExceeded, ex.Message)
                : Problem(StatusCodes.Status409Conflict, ex.Reason switch
                {
                    AiBlock.Disabled => AiErrorCodes.Disabled,
                    AiBlock.UseDisabled => AiErrorCodes.UseDisabled,
                    _ => AiErrorCodes.NotConfigured
                }, ex.Message);
        }
        catch (AiProviderException ex)
        {
            return Problem(StatusCodes.Status502BadGateway, AiErrorCodes.ProviderError, ex.Message);
        }
    }

    private ObjectResult Problem(int status, string code, string message)
    {
        var problem = new ProblemDetails { Status = status, Title = message };
        problem.Extensions["code"] = code;
        return StatusCode(status, problem);
    }
}

public static class AiErrorCodes
{
    public const string SettingsInvalid = "AI_SETTINGS_INVALID";
    public const string NodeRequired = "AI_NODE_REQUIRED";
    public const string Disabled = "AI_DISABLED";
    public const string UseDisabled = "AI_USE_DISABLED";
    public const string NotConfigured = "AI_NOT_CONFIGURED";
    public const string BudgetExceeded = "AI_BUDGET_EXCEEDED";
    public const string ProviderError = "AI_PROVIDER_ERROR";
}
