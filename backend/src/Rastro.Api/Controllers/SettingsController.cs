using Microsoft.AspNetCore.Mvc;
using Rastro.Core.Localization;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Api.Controllers;

// Idioma do Rastro (ADR-009): gravado no config.json e lido pela API, pela CLI e pelo MCP.
// A proteção é a das outras rotas que mudam dado (AllowedHosts e Origin).
[ApiController]
[Route("api/settings")]
public class SettingsController(
    RepositoryConfigFile configFile,
    IConfiguration configuration,
    ILogger<SettingsController> logger) : ControllerBase
{
    // Language é o gravado (nulo = segue o sistema); Effective é o que vale sem cabeçalho Accept-Language.
    public record LanguageView(int? Language, int Effective);

    // Language nulo apaga o idioma gravado; número fora do enum dá LANGUAGE_INVALID.
    public record SetLanguageRequest(int? Language);

    [HttpGet("language")]
    public LanguageView GetLanguage() => View();

    [HttpPut("language")]
    public IActionResult SetLanguage([FromBody] SetLanguageRequest request)
    {
        if (request.Language is { } value && !UiLanguages.IsDefined(value))
            return Error(StatusCodes.Status400BadRequest, ErrorCodes.LanguageInvalid, RequestLanguage());

        configFile.SetLanguage(request.Language is { } v ? ((UiLanguage)v).Code() : null);
        // O file watcher recarrega sozinho, mas com atraso: recarregar aqui deixa o GET seguinte ver a mudança.
        (configuration as IConfigurationRoot)?.Reload();
        logger.LogInformation("Idioma do Rastro alterado pela tela para {Language}.", request.Language is { } l ? ((UiLanguage)l).Code() : "sistema");
        return Ok(View());
    }

    private LanguageView View()
    {
        var saved = configFile.GetLanguage();
        return new LanguageView(saved is { } s ? (int)s : null, (int)(saved ?? UiLanguages.FromSystem()));
    }

    // Texto do erro no idioma do pedido (o RequestLocalization já pôs a cultura na thread da borda da API).
    private static UiLanguage RequestLanguage() => UiLanguages.FromCulture(System.Globalization.CultureInfo.CurrentUICulture);

    private ObjectResult Error(int status, string code, UiLanguage language)
    {
        var problem = new ProblemDetails { Status = status, Title = LocalizedText.Get("Language.Invalid", language) };
        problem.Extensions["code"] = code;
        return StatusCode(status, problem);
    }
}
