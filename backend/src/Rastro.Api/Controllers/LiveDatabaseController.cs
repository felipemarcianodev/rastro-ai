using Microsoft.AspNetCore.Mvc;
using Rastro.Core.Database;
using Rastro.Infrastructure.Database.Live;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Api.Controllers;

// Banco vivo de um repositório (Fase 40b, ADR-006): cadastro da string de conexão (cifrada, nunca
// devolvida), teste e plano estimado de uma consulta, sem executá-la. Só leitura: catálogo e plano,
// nunca os dados das tabelas. AllowedHosts e o Origin valem como em toda rota que muda dado.
[ApiController]
[Route("api/repositories/{name}")]
public class LiveDatabaseController(
    RepositoryCatalog catalog,
    LiveConnectionStore connections,
    LiveDatabaseService live,
    ILogger<LiveDatabaseController> logger) : ControllerBase
{
    [HttpGet("connection")]
    public async Task<ActionResult<LiveConnectionView>> Get(string name, CancellationToken ct) =>
        catalog.RootOf(name) is null ? NotFoundRepository() : Ok(await connections.ViewAsync(name, ct));

    [HttpPut("connection")]
    public async Task<ActionResult<LiveConnectionView>> Save(string name, [FromBody] SaveConnectionRequest request, CancellationToken ct)
    {
        if (catalog.RootOf(name) is null) return NotFoundRepository();
        try
        {
            var view = await connections.SaveAsync(name, request.ConnectionString, ct);
            logger.LogInformation("Conexão com o banco vivo do repositório {Repository} cadastrada (servidor {Server}).", name, view.Server);
            return Ok(view);
        }
        catch (ArgumentException ex)
        {
            // A mensagem é a do Rastro, nunca a do driver: não carrega pedaço da string.
            logger.LogWarning("Conexão com o banco vivo do repositório {Repository} recusada: {Reason}", name, ex.Message);
            return Error(StatusCodes.Status400BadRequest, ErrorCodes.ConnectionInvalid, ex.Message);
        }
    }

    [HttpDelete("connection")]
    public async Task<IActionResult> Remove(string name, CancellationToken ct)
    {
        if (catalog.RootOf(name) is null) return NotFoundRepository();
        await connections.RemoveAsync(name, ct);
        logger.LogInformation("Conexão com o banco vivo do repositório {Repository} removida.", name);
        return NoContent();
    }

    [HttpPost("connection/test")]
    public async Task<ActionResult<LiveConnectionTest>> Test(string name, CancellationToken ct)
    {
        if (catalog.RootOf(name) is null) return NotFoundRepository();
        return await live.TestAsync(name, ct) is { } test
            ? Ok(test)
            : Error(StatusCodes.Status409Conflict, ErrorCodes.ConnectionMissing, "Cadastre a conexão com o banco vivo antes.");
    }

    // Plano estimado (SET SHOWPLAN_XML ON): a consulta passa antes pelo leitor de T-SQL e nunca é executada.
    [HttpPost("schema/plan")]
    public async Task<ActionResult<QueryPlan>> Plan(string name, [FromBody] AnalyzeQueryRequest request, CancellationToken ct)
    {
        if (catalog.RootOf(name) is null) return NotFoundRepository();
        if (string.IsNullOrWhiteSpace(request.Sql) || request.Sql.Length > QueryLimits.MaxSqlLength)
            return Error(StatusCodes.Status400BadRequest, ErrorCodes.QueryInvalid, "Cole uma consulta de até 100 mil caracteres.");

        var result = await live.PlanAsync(name, request.Sql, ct);
        return result.Outcome switch
        {
            LivePlanOutcome.Ok => Ok(result.Plan),
            LivePlanOutcome.NoConnection => Error(StatusCodes.Status409Conflict, ErrorCodes.ConnectionMissing, "Cadastre a conexão com o banco vivo antes."),
            LivePlanOutcome.NotAllowed => Error(StatusCodes.Status400BadRequest, ErrorCodes.PlanNotAllowed, result.Reason ?? "Consulta recusada."),
            _ => Error(StatusCodes.Status502BadGateway, ErrorCodes.LiveDatabaseError, $"O banco vivo não respondeu: {result.Reason}.")
        };
    }

    private ObjectResult NotFoundRepository() =>
        Error(StatusCodes.Status404NotFound, ErrorCodes.RepositoryNotFound, "Repositório não cadastrado.");

    private ObjectResult Error(int status, string code, string message)
    {
        var problem = new ProblemDetails { Status = status, Title = message };
        problem.Extensions["code"] = code;
        return StatusCode(status, problem);
    }
}
