// Acumula nós e arestas na ordem em que o percurso os encontra (saída determinística).
// Espelha o GraphBuilder do C#, e os valores dos enums são os mesmos (viajam como int).

export const NodeKind = Object.freeze({
  Entry: 0,
  Method: 1,
  Decision: 2,
  Loop: 3,
  Try: 4,
  Return: 5,
  Throw: 6,
  External: 7,
  Unresolved: 8,
  Jump: 9,
});

export const UnresolvedReason = Object.freeze({
  MultipleImplementations: 0,
  NoImplementation: 1,
  DynamicDispatch: 2,
  Delegate: 3,
  MessageDispatch: 4,
  Reflection: 5,
  DepthLimit: 6,
  RouteNotResolved: 7,
  NoMatchingEndpoint: 8,
  MultipleEndpoints: 9,
});

export const EdgeConfidence = Object.freeze({ Extracted: 0, Inferred: 1, Confirmed: 2 });

// Versão do esquema do grafo (RastroSchema.Version no C#). Precisa andar junto com ela.
export const SCHEMA_VERSION = 2;

export const MAX_LABEL_LENGTH = 120;

export function truncar(texto) {
  return texto.length <= MAX_LABEL_LENGTH ? texto : texto.slice(0, MAX_LABEL_LENGTH - 1) + '…';
}

// Remove as chaves indefinidas: o JSON do C# omite os campos nulos.
function semIndefinidos(objeto) {
  return Object.fromEntries(Object.entries(objeto).filter(([, valor]) => valor !== undefined && valor !== null));
}

export class GraphBuilder {
  #nos = new Map();
  #arestas = [];
  #proximaOrdem = new Map();
  #chaves = new Set();

  temNo(id) {
    return this.#nos.has(id);
  }

  addNo({ id, kind, label, symbol, location, condition, reason, detail, project }) {
    if (this.#nos.has(id)) return;
    this.#nos.set(id, semIndefinidos({ id, kind, label, symbol, location, condition, reason, detail, project }));
  }

  // Mesma chamada repetida no mesmo ramo vira uma aresta só (mesmo from, to e label).
  // `confianca` só sai de Extracted quando o vínculo é inferido (ex.: ação do usuário num modal).
  addAresta(from, to, label, location, http, confianca = EdgeConfidence.Extracted) {
    const chave = JSON.stringify([from, to, label ?? null]);
    if (this.#chaves.has(chave)) return;
    this.#chaves.add(chave);

    const order = (this.#proximaOrdem.get(from) ?? 0) + 1;
    this.#proximaOrdem.set(from, order);
    this.#arestas.push(
      semIndefinidos({ from, to, order, confidence: confianca, label, location, http }),
    );
  }

  build(entryPoint, entryNodeId) {
    return {
      schemaVersion: SCHEMA_VERSION,
      entryPoint,
      entryNodeId,
      nodes: [...this.#nos.values()],
      edges: this.#arestas,
    };
  }
}
