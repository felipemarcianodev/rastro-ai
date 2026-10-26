import { FlowGraph } from '../models';

// Sem acento e sem caixa: "notificacao" acha "NotificaçãoService".
export function normalizeText(text: string): string {
  return text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

// Busca no grafo (Ctrl+F): passos cujo nome ou símbolo têm todos os termos, na ordem do grafo
// (a da extração, que segue a execução). Consulta vazia não acha nada.
export function searchNodes(graph: FlowGraph, query: string): string[] {
  const terms = normalizeText(query).split(/\s+/).filter(Boolean);
  if (terms.length === 0) return [];
  return graph.nodes
    .filter((node) => {
      const text = normalizeText(`${node.label} ${node.symbol ?? ''}`);
      return terms.every((term) => text.includes(term));
    })
    .map((node) => node.id);
}
