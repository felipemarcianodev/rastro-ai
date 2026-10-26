namespace Rastro.Core.Graph;

// Quem propôs algo ao núcleo (ADR-001). O núcleo conhece a origem, nunca a IA em si.
public enum ProposalOrigin
{
    Manual = 0,

    // Modo agente: um agente externo via Rastro.Mcp.
    Agent = 1,

    // Modo provedor: Claude, OpenAI ou Gemini via Rastro.Ai.
    Provider = 2
}
