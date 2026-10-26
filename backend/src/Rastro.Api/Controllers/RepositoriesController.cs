using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Api.Controllers;

// Cadastro de repositórios pela tela (ADR-003). A tela envia nome e pasta; comando de build e de
// testes nunca entra pela API (só pelo "rastro repo add --build/--testes").
[ApiController]
[Route("api/repositories")]
public class RepositoriesController(
    RepositoryCatalog catalog,
    IOptionsMonitor<RastroOptions> options,
    RepositoryConfigFile configFile,
    IConfiguration configuration,
    ILogger<RepositoriesController> logger) : ControllerBase
{
    [HttpGet]
    public IReadOnlyList<RepositoryItem> List() =>
        options.CurrentValue.Repositories
            .Where(r => !string.IsNullOrWhiteSpace(r.Name) && !string.IsNullOrWhiteSpace(r.Path))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r =>
            {
                var path = Path.GetFullPath(r.Path);
                return new RepositoryItem(r.Name, path, HasGit(path),
                    !string.IsNullOrWhiteSpace(r.BuildCommand), !string.IsNullOrWhiteSpace(r.TestCommand));
            })
            .ToList();

    [HttpPost]
    public IActionResult Add([FromBody] AddRepositoryRequest request)
    {
        if (!RepositoryConfigFile.IsValidName(request.Name))
            return Error(StatusCodes.Status400BadRequest, ErrorCodes.RepositoryNameInvalid, "Nome de repositório inválido.");
        if (string.IsNullOrWhiteSpace(request.Path) || !Path.IsPathFullyQualified(request.Path))
            return Error(StatusCodes.Status400BadRequest, ErrorCodes.RepositoryPathInvalid, "Informe o caminho completo da pasta.");

        var result = configFile.Add(request.Name!, request.Path);
        switch (result.Outcome)
        {
            case RepositoryConfigOutcome.FolderNotFound:
                return Error(StatusCodes.Status400BadRequest, ErrorCodes.RepositoryFolderNotFound, "A pasta não existe.");
            case RepositoryConfigOutcome.Exists:
                return Error(StatusCodes.Status409Conflict, ErrorCodes.RepositoryExists, "Já existe um repositório com esse nome.");
            case RepositoryConfigOutcome.InvalidName:
                return Error(StatusCodes.Status400BadRequest, ErrorCodes.RepositoryNameInvalid, "Nome de repositório inválido.");
        }

        ReloadConfiguration();
        logger.LogInformation("Repositório {Repository} cadastrado pela tela.", request.Name);
        var item = new RepositoryItem(request.Name!, result.Path!, result.HasGit, false, false);
        return Created($"api/repositories/{request.Name}", item);
    }

    [HttpDelete("{name}")]
    public IActionResult Remove(string name)
    {
        var result = configFile.Remove(name);
        if (result.Outcome == RepositoryConfigOutcome.NotFound)
            return Error(StatusCodes.Status404NotFound, ErrorCodes.RepositoryNotFound, "Repositório não cadastrado.");

        ReloadConfiguration();
        logger.LogInformation("Repositório {Repository} removido pela tela.", name);
        return NoContent();
    }

    [HttpGet("{name}/solutions")]
    public ActionResult<IReadOnlyList<string>> Solutions(string name) =>
        catalog.Solutions(name) is { } solutions ? Ok(solutions) : NotFound();

    // O file watcher recarrega o config.json sozinho, mas com atraso. Recarregar aqui, de forma
    // síncrona, faz o GET logo depois do POST ou do DELETE já ver a mudança.
    private void ReloadConfiguration() => (configuration as IConfigurationRoot)?.Reload();

    private static bool HasGit(string path) =>
        Directory.Exists(Path.Combine(path, ".git")) || System.IO.File.Exists(Path.Combine(path, ".git"));

    private ObjectResult Error(int status, string code, string message)
    {
        var problem = new ProblemDetails { Status = status, Title = message };
        problem.Extensions["code"] = code;
        return StatusCode(status, problem);
    }
}
