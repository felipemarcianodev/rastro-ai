using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using Rastro.Core.Localization;
using Rastro.Infrastructure.Configuration;

namespace Rastro.Api;

// Idioma de cada requisição (ADR-009), nesta ordem: Accept-Language (a tela sempre manda), o idioma gravado
// no config.json, o idioma do sistema operacional e, por reserva, o en-US (o DefaultRequestCulture).
// Sem consulta nem cookie. Aceita o prefixo: pt-PT vira Portuguese e es-MX vira Spanish.
public class UiLanguageRequestCultureProvider : RequestCultureProvider
{
    public static readonly UiLanguage[] Supported = [.. UiLanguages.All];

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var language = FromHeader(httpContext)
            ?? FromConfig(httpContext)
            ?? SystemLanguage();
        if (language is null) return NullProviderCultureResult;

        var code = language.Value.Code();
        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(code, code));
    }

    private static UiLanguage? FromHeader(HttpContext httpContext)
    {
        var accepted = httpContext.Request.GetTypedHeaders().AcceptLanguage;
        if (accepted is null || accepted.Count == 0) return null;
        foreach (var value in accepted.OrderByDescending(v => v.Quality ?? 1.0))
        {
            if (value.Quality == 0 || value.Value.Value is null) continue;
            if (UiLanguages.FromCode(value.Value.Value) is { } language) return language;
        }
        return null;
    }

    private static UiLanguage? FromConfig(HttpContext httpContext) =>
        UiLanguages.FromCode(httpContext.RequestServices.GetRequiredService<IOptionsMonitor<RastroOptions>>().CurrentValue.Language);

    // Só vale quando o sistema está num dos três idiomas; senão cai no en-US reserva.
    private static UiLanguage? SystemLanguage() => UiLanguages.FromCode(CultureInfo.CurrentUICulture.Name);
}
