namespace Rastro.Core.Graph;

// Grau de confiança de uma aresta do grafo (ADR-001). O usuário sempre vê este valor.
public enum EdgeConfidence
{
    // Veio do compilador (Roslyn): certa.
    Extracted = 0,

    // Proposta por IA (agente ou provedor) e aprovada pelo validador de prova: provável.
    Inferred = 1,

    // Conferida e aceita pelo usuário: certa.
    Confirmed = 2
}
