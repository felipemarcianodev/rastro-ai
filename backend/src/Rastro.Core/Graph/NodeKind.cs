namespace Rastro.Core.Graph;

// Tipo de nó do grafo de fluxo. Valores explícitos: viajam como int no JSON.
public enum NodeKind
{
    // Método de entrada do fluxo (controller action, job, handler).
    Entry = 0,

    // Método do próprio código, percorrido.
    Method = 1,

    // Bifurcação: if/else ou switch, com a condição em texto.
    Decision = 2,

    // Laço: foreach, for, while, do.
    Loop = 3,

    // Bloco try com seus catch e finally.
    Try = 4,

    // Saída antecipada (return dentro de uma bifurcação ou laço).
    Return = 5,

    // Exceção lançada.
    Throw = 6,

    // Chamada para fora do código (HTTP, banco, SDK) feita por uma dependência.
    External = 7,

    // Chamada que o compilador não consegue resolver sozinho. O motivo fica em UnresolvedReason.
    Unresolved = 8,

    // Desvio dentro de um laço: break ou continue.
    Jump = 9
}
