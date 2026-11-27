import { FlowGraph, FlowNode, NodeKind } from '../models';
import { translatedRecord } from './translated';

// Desenho do grafo: da esquerda para a direita (padrão), em linha do tempo (Fase 20, no lugar do
// antigo "de cima para baixo"), em árvore (a mesma regra, com ligações retas), consolidado (Fase 28:
// o Mermaid da visão consolidada, que não passa pelo canvas) ou mapa (Fase 31: faixas por sistema,
// também fora do canvas). O desenho fica em timeline-layout.ts;
// aqui, a direção e o tamanho de cada passo.
// O valor 1 era o vertical: quem o tinha guardado passa a abrir a linha do tempo.
export enum LayoutDirection {
  Horizontal = 0,
  Timeline = 1,
  Tree = 2,
  Consolidated = 3,
  Map = 4,
  Business = 5,
  Drawio = 6,
}

export const LAYOUT_DIRECTION_SLUG: Record<LayoutDirection, string> = {
  [LayoutDirection.Horizontal]: 'horizontal',
  [LayoutDirection.Timeline]: 'linha-do-tempo',
  [LayoutDirection.Tree]: 'arvore',
  [LayoutDirection.Consolidated]: 'consolidado',
  [LayoutDirection.Map]: 'mapa',
  [LayoutDirection.Business]: 'negocio',
  [LayoutDirection.Drawio]: 'drawio',
};

// Desenho de um link direto (?desenho=<slug>); slug desconhecido não escolhe nada.
export function directionFromSlug(slug: string | null | undefined): LayoutDirection | null {
  if (!slug) return null;
  const found = Object.entries(LAYOUT_DIRECTION_SLUG).find(([, value]) => value === slug);
  return found ? (Number(found[0]) as LayoutDirection) : null;
}

// Desenhos que não passam pelo canvas: a página os desenha no lugar dele.
export function isOutsideCanvas(direction: LayoutDirection): boolean {
  return direction === LayoutDirection.Consolidated || direction === LayoutDirection.Map || direction === LayoutDirection.Business || direction === LayoutDirection.Drawio;
}

// Chaves de tradução do rótulo de cada desenho (traduzidas na hora de usar).
export const LAYOUT_DIRECTION_LABEL_KEY: Record<LayoutDirection, string> = {
  [LayoutDirection.Horizontal]: 'graph.layout.horizontal',
  [LayoutDirection.Timeline]: 'graph.layout.timeline',
  [LayoutDirection.Tree]: 'graph.layout.tree',
  [LayoutDirection.Consolidated]: 'graph.layout.consolidated',
  [LayoutDirection.Map]: 'graph.layout.map',
  [LayoutDirection.Business]: 'graph.layout.business',
  [LayoutDirection.Drawio]: 'graph.layout.drawio',
};

// O texto já traduzido, para quem lê o rótulo direto (traduz na hora da leitura).
export const LAYOUT_DIRECTION_LABEL = translatedRecord(LAYOUT_DIRECTION_LABEL_KEY);

export const LAYOUT_DIRECTION_GLYPH: Record<LayoutDirection, string> = {
  [LayoutDirection.Horizontal]: '⇉',
  [LayoutDirection.Timeline]: '↓',
  [LayoutDirection.Tree]: '┣',
  [LayoutDirection.Consolidated]: '◇',
  [LayoutDirection.Map]: '▦',
  [LayoutDirection.Business]: '◆',
  [LayoutDirection.Drawio]: '◫',
};

const DIRECTION_KEY = 'rastro.layoutDirection';

// A escolha do desenho fica no navegador; valor desconhecido (ou armazenamento bloqueado) é o horizontal.
export function readLayoutDirection(): LayoutDirection {
  try {
    const saved = Number(localStorage.getItem(DIRECTION_KEY));
    return saved === LayoutDirection.Timeline || saved === LayoutDirection.Tree || isOutsideCanvas(saved) ? saved : LayoutDirection.Horizontal;
  } catch {
    return LayoutDirection.Horizontal;
  }
}

export function saveLayoutDirection(direction: LayoutDirection): void {
  try {
    localStorage.setItem(DIRECTION_KEY, String(direction));
  } catch {
    // Armazenamento bloqueado: a escolha vale só nesta visita.
  }
}

const DRAWIO_ORIGIN_KEY = 'rastro.drawioOrigin';

// O desenho de onde se abriu o draw.io (Fase 64): ele é a origem do XML e o desenho que abre primeiro quando o
// draw.io é a escolha guardada. Valor desconhecido ou o próprio draw.io vale o grafo horizontal.
export function readDrawioOrigin(): LayoutDirection {
  try {
    const saved = Number(localStorage.getItem(DRAWIO_ORIGIN_KEY));
    return saved !== LayoutDirection.Drawio && Object.values(LayoutDirection).includes(saved) ? saved : LayoutDirection.Horizontal;
  } catch {
    return LayoutDirection.Horizontal;
  }
}

export function saveDrawioOrigin(direction: LayoutDirection): void {
  try {
    localStorage.setItem(DRAWIO_ORIGIN_KEY, String(direction));
  } catch {
    // Armazenamento bloqueado: a origem vale só nesta visita.
  }
}

export interface NodeBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

// Largura do texto em fonte mono 13px: ~7,8px por caractere, mais padding, glifo e folga.
const CHAR_PX = 7.8;
const CHROME_PX = 52;
const FOLD_PX = 24;
const GUARD_GAP_PX = 12;
// Teto: acima disso o nó vira uma tripa e o grafo espalha; o dono do método encolhe com reticências.
export const MAX_NODE_WIDTH = 440;
// Validação num passo só (Fase 19) leva a condição e o desfecho na mesma linha: teto maior.
export const MAX_GUARD_WIDTH = 640;
// Separador entre a condição e o desfecho no texto da validação (largura, SVG e Mermaid).
export const GUARD_ARROW = ' → ';

// Tamanho de cada tipo de nó. A largura acompanha o nome (até o teto), para o dono do método
// não sumir em "A…" quando o nome do método é longo.
// "extraPx": o que a tela acrescenta ao passo (a etiqueta "banco", "mensagem", Fase 23).
export function nodeSize(node: FlowNode, extraPx = 0): { width: number; height: number } {
  const base = baseSize(node);
  // Método ganha o botão ▾/▸ de abrir e fechar o detalhe (Fase 18).
  const fold = node.kind === NodeKind.Method ? FOLD_PX : 0;
  // A validação ganha o recuo antes do desfecho (padding do .flow-node-outcome).
  const guard = node.kind === NodeKind.Decision && node.label.includes(GUARD_ARROW);
  const wanted = Math.ceil(CHROME_PX + fold + extraPx + (guard ? GUARD_GAP_PX : 0) + node.label.length * CHAR_PX);
  return { width: Math.max(base.width, Math.min(wanted, (guard ? MAX_GUARD_WIDTH : MAX_NODE_WIDTH) + extraPx)), height: base.height };
}

function baseSize(node: FlowNode): { width: number; height: number } {
  switch (node.kind) {
    case NodeKind.Entry:
    case NodeKind.Method:
    case NodeKind.Unresolved:
      return { width: 252, height: 56 };
    case NodeKind.External:
      return { width: 232, height: 40 };
    case NodeKind.Decision:
    case NodeKind.Loop:
      return { width: 236, height: 44 };
    case NodeKind.Try:
      return { width: 120, height: 40 };
    default:
      return { width: 176, height: 40 };
  }
}
