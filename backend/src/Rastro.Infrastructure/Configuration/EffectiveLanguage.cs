using Rastro.Core.Localization;

namespace Rastro.Infrastructure.Configuration;

// Idioma que vale quando não há um pedido com Accept-Language (worker de validação, MCP, CLI): o gravado no
// config.json e, sem ele, o do sistema operacional (inglês quando não é um dos três). ADR-009.
public static class EffectiveLanguage
{
    public static UiLanguage Of(RastroOptions options) => UiLanguages.FromCode(options.Language) ?? UiLanguages.FromSystem();
}
