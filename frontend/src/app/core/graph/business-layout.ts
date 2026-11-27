import { BusinessFlow, BusinessKind, BusinessStep } from '../models';
import { NodeBox } from './layout';

// Desenho da visão de negócio (Fase 57): uma camada por distância da entrada, de cima para baixo.
// Puro e determinístico: o mesmo fluxo dá sempre o mesmo desenho.

// Medidas em px. A decisão é um losango e precisa de espaço para o texto.
export const BUSINESS_BOX = {
  decision: { width: 240, height: 110 },
  decisionLong: { width: 280, height: 150 },
  // 88 px: duas linhas de texto, a referência em até duas linhas e o selo.
  step: { width: 220, height: 88 },
  gapY: 60,
  gapX: 40,
  grid: 10,
  // Acima deste tamanho de texto, a decisão usa o losango maior.
  longLabel: 40,
} as const;

export interface BusinessGeometry {
  boxes: Map<string, NodeBox>;
  width: number;
  height: number;
}

// Estimativa do texto do balão (D-03). O layout sai antes de o DOM existir, então a altura vem da conta: caracteres
// por linha = largura útil / largura média do caractere em --text-xs (12 px). Mesmo valor do SVG exportado.
export const BUSINESS_TEXT = {
  charWidth: 6.4,
  lineHeight: 16,
  // Acima disto o texto corta com reticências no balão (o texto inteiro fica na dica).
  maxLines: 6,
  // Largura que o balão de passo gasta fora do texto: padding (2 x 12), glifo (18) e espaço (6).
  stepInset: 42,
  // No losango: padding lateral (2 x 56), glifo e espaço.
  decisionInset: 136,
  // Linhas que o balão-base já comporta sem crescer.
  stepBaseLines: 2,
  decisionBaseLines: 3,
} as const;

function boxBase(step: BusinessStep): { width: number; height: number } {
  if (step.kind !== BusinessKind.Decision) return BUSINESS_BOX.step;
  return step.label.length > BUSINESS_BOX.longLabel ? BUSINESS_BOX.decisionLong : BUSINESS_BOX.decision;
}

// Linhas que o texto do passo ocupa no balão, de 1 até o teto. Determinístico: só depende do texto e do tipo.
export function businessTextLines(step: BusinessStep): number {
  const decision = step.kind === BusinessKind.Decision;
  const inset = decision ? BUSINESS_TEXT.decisionInset : BUSINESS_TEXT.stepInset;
  const perLine = Math.max(1, Math.floor((boxBase(step).width - inset) / BUSINESS_TEXT.charWidth));
  return Math.min(BUSINESS_TEXT.maxLines, Math.max(1, Math.ceil(step.label.length / perLine)));
}

export function businessBoxSize(step: BusinessStep): { width: number; height: number } {
  const base = boxBase(step);
  const lines = businessTextLines(step);
  const decision = step.kind === BusinessKind.Decision;
  const extra = Math.max(0, lines - (decision ? BUSINESS_TEXT.decisionBaseLines : BUSINESS_TEXT.stepBaseLines));
  if (extra === 0) return base;
  // O losango perde metade da altura útil nas pontas: cada linha extra pede duas de altura.
  return { width: base.width, height: base.height + extra * BUSINESS_TEXT.lineHeight * (decision ? 2 : 1) };
}

const snap = (value: number): number => Math.ceil(value / BUSINESS_BOX.grid) * BUSINESS_BOX.grid;

export interface LayoutPoint {
  x: number;
  y: number;
}

// Encaixe na grade ao mover um balão ou um ponto de dobra (o ponto mais próximo, e não para cima como o layout).
export const snapToGrid = (value: number): number => Math.round(value / BUSINESS_BOX.grid) * BUSINESS_BOX.grid;

// Chave de uma ligação nas posições salvas: de onde sai e aonde chega.
export const edgeKey = (from: string, to: string): string => `${from}\u0000${to}`;

// A posição salva vence a do layout automático; passo sem posição salva fica onde o layout o pôs.
export function withSavedPositions(geometry: BusinessGeometry, saved: ReadonlyMap<string, LayoutPoint>): BusinessGeometry {
  if (saved.size === 0) return geometry;
  const boxes = new Map<string, NodeBox>();
  let width = 0;
  let height = 0;
  for (const [id, box] of geometry.boxes) {
    const at = saved.get(id);
    const placed = at ? { ...box, x: at.x, y: at.y } : box;
    boxes.set(id, placed);
    width = Math.max(width, placed.x + placed.width);
    height = Math.max(height, placed.y + placed.height);
  }
  return { boxes, width, height };
}

// Posição do rótulo ao longo da ligação (0 = origem, 1 = destino): perto da chegada. Quando duas ligações saem da
// mesma porta, o trecho do meio é comum às duas, e só o final mostra de que ramo é o rótulo. Assim cada rótulo fica
// centrado sobre o seu destino; os irmãos (casos de um switch) não se sobrepõem porque o rótulo não passa da largura
// do balão (`--biz-label-max`, em _business.scss). Escalonar a posição ao longo da ligação foi tentado e não
// resolve: nas ligações dos lados quase todo o caminho é o trecho horizontal comum, e os rótulos caem na mesma altura.
export const BRANCH_LABEL_POSITION = 0.88;

// O vizinho de um passo na ordem do fluxo (a ordem da lista de passos), ou null nas pontas. Sem passo atual, o
// primeiro (ou o último, voltando).
export function flowNeighbour(order: readonly string[], current: string | null, direction: 1 | -1): string | null {
  const at = current === null ? -1 : order.indexOf(current);
  if (at < 0) return (direction === 1 ? order[0] : order[order.length - 1]) ?? null;
  return order[at + direction] ?? null;
}

// Ligações que fecham um ciclo (a ponta chega a um passo que ainda está no caminho da busca) ficam
// de fora do cálculo das camadas. A busca parte da entrada e depois dos passos que sobraram, na ordem da lista.
function backEdges(flow: BusinessFlow, order: readonly string[]): Set<number> {
  const out = new Map<string, number[]>();
  flow.edges.forEach((edge, i) => out.set(edge.from, [...(out.get(edge.from) ?? []), i]));
  const state = new Map<string, 1 | 2>();
  const back = new Set<number>();
  const visit = (id: string) => {
    state.set(id, 1);
    for (const i of out.get(id) ?? []) {
      const to = flow.edges[i].to;
      if (state.get(to) === 1) back.add(i);
      else if (!state.has(to)) visit(to);
    }
    state.set(id, 2);
  };
  for (const id of order) if (!state.has(id)) visit(id);
  return back;
}

export function businessLayout(flow: BusinessFlow): BusinessGeometry {
  const ids = new Set(flow.steps.map((s) => s.id));
  const edges = flow.edges.filter((e) => ids.has(e.from) && ids.has(e.to) && e.from !== e.to);
  const clean: BusinessFlow = { ...flow, edges };

  // Raízes da busca: os passos Start primeiro, depois os demais na ordem da lista.
  const starts = flow.steps.filter((s) => s.kind === BusinessKind.Start).map((s) => s.id);
  const order = [...starts, ...flow.steps.map((s) => s.id).filter((id) => !starts.includes(id))];
  const back = backEdges(clean, order);
  const forward = edges.filter((_, i) => !back.has(i));

  // Camada: maior distância da raiz pelas ligações que sobraram (sem ciclo).
  const layer = new Map<string, number>(flow.steps.map((s) => [s.id, 0]));
  const incoming = new Map<string, number>(flow.steps.map((s) => [s.id, 0]));
  for (const e of forward) incoming.set(e.to, (incoming.get(e.to) ?? 0) + 1);
  const ready = flow.steps.map((s) => s.id).filter((id) => incoming.get(id) === 0);
  for (let head = 0; head < ready.length; head++) {
    const id = ready[head];
    for (const e of forward) {
      if (e.from !== id) continue;
      layer.set(e.to, Math.max(layer.get(e.to)!, layer.get(id)! + 1));
      incoming.set(e.to, incoming.get(e.to)! - 1);
      if (incoming.get(e.to) === 0) ready.push(e.to);
    }
  }

  const depth = flow.steps.reduce((max, s) => Math.max(max, layer.get(s.id)!), -1) + 1;
  const rows: string[][] = Array.from({ length: depth }, () => []);
  const placed = new Set<string>();
  const put = (id: string) => {
    if (placed.has(id)) return;
    placed.add(id);
    rows[layer.get(id)!].push(id);
  };
  // Camada 0 na ordem da lista (a entrada primeiro); as seguintes na ordem das ligações de quem chega,
  // o que deixa os ramos de uma decisão lado a lado.
  flow.steps.filter((s) => layer.get(s.id) === 0).forEach((s) => put(s.id));
  for (let l = 0; l < depth - 1; l++) {
    for (const id of rows[l]) for (const e of forward) if (e.from === id && layer.get(e.to)! > l) put(e.to);
    for (const s of flow.steps) if (layer.get(s.id) === l + 1) put(s.id);
  }

  const byId = new Map(flow.steps.map((s) => [s.id, s]));
  const rowWidth = (row: string[]) => row.reduce((sum, id) => sum + businessBoxSize(byId.get(id)!).width, 0) + Math.max(0, row.length - 1) * BUSINESS_BOX.gapX;
  const width = rows.reduce((max, row) => Math.max(max, rowWidth(row)), 0);

  const boxes = new Map<string, NodeBox>();
  let y = 0;
  for (const row of rows) {
    let x = snap((width - rowWidth(row)) / 2);
    let tallest = 0;
    for (const id of row) {
      const size = businessBoxSize(byId.get(id)!);
      boxes.set(id, { x, y, width: size.width, height: size.height });
      x += size.width + BUSINESS_BOX.gapX;
      tallest = Math.max(tallest, size.height);
    }
    y = snap(y + tallest + BUSINESS_BOX.gapY);
  }
  const height = [...boxes.values()].reduce((max, b) => Math.max(max, b.y + b.height), 0);
  return { boxes, width, height };
}
