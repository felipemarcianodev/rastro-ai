using System.Resources;
using Rastro.Core.Localization;

namespace Rastro.Mcp;

// Texto que as ferramentas do MCP DEVOLVEM ao agente, no idioma de quem usa o Rastro (ADR-009): Resources/Messages.resx
// (neutro, inglês), Messages.pt-BR.resx e Messages.es.resx. As instruções do MCP e as descrições das ferramentas
// ficam em inglês fixo (McpInstructions e os atributos [Description]): o agente as lê uma vez, ao conectar.
// Cultura sempre explícita, como no LocalizedText do núcleo.
public static class McpText
{
    private static readonly ResourceManager Manager =
        new("Rastro.Mcp.Resources.Messages", typeof(McpText).Assembly);

    public static string Get(string key, UiLanguage language, params object[] args)
    {
        var culture = language.Culture();
        var template = Manager.GetString(key, culture) ?? key;
        return args.Length == 0 ? template : string.Format(culture, template, args);
    }
}
