using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rastro.Api.Analysis;
using Rastro.Core.Validation;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Validation;

namespace Rastro.Api.Controllers;

public record ValidationView(
    Guid Id,
    Guid SurveyId,
    Guid? NewSurveyId,
    ValidationStatus Status,
    string? Error,
    ChangeValidationResult? Result,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ApprovedAt,
    bool HasDocument)
{
    public static ValidationView From(SurveyValidation v) => new(
        v.Id, v.SurveyId, v.NewSurveyId, v.Status, v.Error, v.ReadResult(), v.CreatedAt, v.CompletedAt, v.ApprovedAt, v.DocumentMarkdown is not null);
}

// Validação da mudança (Fase 9): pedir, acompanhar, aprovar e baixar a documentação do fluxo.
[ApiController]
public class ValidationsController(RastroDbContext db, ChangeValidationService validations, ValidationQueue queue) : ControllerBase
{
    private const int ListLimit = 20;

    [HttpPost("api/surveys/{surveyId:guid}/validations")]
    public async Task<ActionResult<ValidationView>> Create(Guid surveyId, CancellationToken ct)
    {
        var outcome = await validations.RequestAsync(surveyId, ct);
        if (outcome.Validation is not { } validation)
        {
            return outcome.Error switch
            {
                ValidationRequestError.DraftRequired => Error(ErrorCodes.DraftRequired, "Desenhe a mudança no modo Editar fluxo antes de validar."),
                ValidationRequestError.AlreadyRunning => Error(ErrorCodes.ValidationRunning, "Já existe uma validação em andamento para este levantamento."),
                _ => NotFound()
            };
        }

        await queue.EnqueueAsync(validation.Id, ct);
        return AcceptedAtAction(nameof(Get), new { id = validation.Id }, ValidationView.From(validation));
    }

    [HttpGet("api/surveys/{surveyId:guid}/validations")]
    public async Task<IReadOnlyList<ValidationView>> List(Guid surveyId, CancellationToken ct)
    {
        var list = await db.SurveyValidations.AsNoTracking()
            .Where(v => v.SurveyId == surveyId)
            .OrderByDescending(v => v.CreatedAt)
            .Take(ListLimit)
            .ToListAsync(ct);
        return list.Select(ValidationView.From).ToList();
    }

    [HttpGet("api/validations/{id:guid}")]
    public async Task<ActionResult<ValidationView>> Get(Guid id, CancellationToken ct)
    {
        var validation = await db.SurveyValidations.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
        return validation is null ? NotFound() : ValidationView.From(validation);
    }

    // Aprovação do usuário: gera a documentação do fluxo. Validação reprovada não é aprovada.
    [HttpPost("api/validations/{id:guid}/approve")]
    public async Task<ActionResult<ValidationView>> Approve(Guid id, CancellationToken ct)
    {
        if (!await db.SurveyValidations.AnyAsync(v => v.Id == id, ct)) return NotFound();

        var approved = await validations.ApproveAsync(id, ct);
        return approved is null
            ? Error(ErrorCodes.ValidationNotApprovable, "Só uma validação concluída e não reprovada pode ser aprovada.")
            : ValidationView.From(approved);
    }

    [HttpGet("api/validations/{id:guid}/document")]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct)
    {
        var validation = await db.SurveyValidations.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
        if (validation?.DocumentMarkdown is null) return NotFound();

        return File(Encoding.UTF8.GetBytes(validation.DocumentMarkdown), "text/markdown; charset=utf-8", $"fluxo-{id:N}.md");
    }

    private BadRequestObjectResult Error(string code, string message)
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = message };
        problem.Extensions["code"] = code;
        return BadRequest(problem);
    }
}
