import { FlowEdge, FlowGraph, FlowNode, NodeKind } from '../models';
import { NodeBox, nodeSize } from './layout';
import { plainBranch } from './plain';
import { BLOCK_INDENT, BLOCK_PAD, BranchLabel, isSection } from './branches';

// Linha do tempo (Fase 20; regra de todo desenho desde a Fase 21). Os passos de um método seguem
// um depois do outro, na ordem do código, ligados por uma linha ("depois"). O que um método chama,
// o corpo do "if" e o do laço saem como galho, e o passo seguinte continua logo depois do
// anterior, sem ser empurrado pelo galho. O "try" vira bloco, com cada catch como seção.
// Determinística e sem ELK: a ordem vem do Order das ligações, e o ramo, do Label.
//
// O mesmo desenho serve aos dois sentidos. Para baixo (vertical e árvore): os passos descem e os
// galhos saem para a direita. Para a direita (horizontal): os passos seguem para a direita e os
// galhos descem. Por dentro, tudo é medido em "u" (o sentido dos passos) e "v" (o dos galhos).

export enum FlowAxis {
  Down = 0,
  Right = 1,
}

interface AxisGaps {
  // Entre um passo e o seguinte.
  step: number;
  // Entre a linha dos passos e a dos galhos dela.
  branch: number;
}

const GAPS: Record<FlowAxis, AxisGaps> = {
  [FlowAxis.Down]: { step: 20, branch: 72 },
  [FlowAxis.Right]: { step: 48, branch: 44 },
};

// Altura do cabeçalho de um galho ("Se sim", "Se não", "Caso ...") quando ele fica em cima do
// passo (para baixo). Para a direita, ele fica à esquerda do passo e ocupa a largura do texto.
export const TIMELINE_HEAD = 22;
// Cabeçalho de seção do bloco ("catch (Exception)") para baixo; para a direita, a largura do texto.
// Para baixo, a linha do texto mais a folga do número do primeiro passo, que fica logo acima dele.
const SECTION_HEAD = 32;
const HEAD_CHAR_PX = 7;
const LOOP_HEAD_GAP = 10;
const HEAD_CHROME_PX = 20;

// Portas do passo. "in": entrada do galho; "top": entrada do passo seguinte (a linha do tempo);
// "out": saída para o passo seguinte; "side": saída do galho. Para baixo ficam à esquerda, em
// cima, embaixo e à direita; para a direita, em cima, à esquerda, à direita e embaixo.
export const Port = {
  In: 'in:',
  Top: 'top:',
  Out: 'out:',
  Side: 'side:',
} as const;

// Cabeçalho escrito junto do primeiro passo do galho, já em português.
export interface TimelineHead {
  x: number;
  y: number;
  label: string;
}

// Fundo atrás de um trecho do fluxo: o bloco do "try" (com o começo de cada seção), desde a
// Fase 23 a moldura do laço (o que se repete, com o cabeçalho "Repete ...") e, desde a Fase 25,
// a moldura de um sistema além do primeiro (cabeçalho "<repositório> · .NET").
export enum BlockKind {
  Try = 0,
  Loop = 1,
  System = 2,
}

export const BLOCK_KIND_SLUG: Record<BlockKind, string> = {
  [BlockKind.Try]: 'try',
  [BlockKind.Loop]: 'loop',
  [BlockKind.System]: 'system',
};

// Cabeçalho da moldura do sistema. A plataforma é sempre .NET: o Rastro só lê C# nesta fase.
export function systemLabel(system: string): string {
  return `${system} · .NET`;
}

export interface FlowBlock {
  id: string;
  kind: BlockKind;
  label?: string;
  x: number;
  y: number;
  width: number;
  height: number;
  sections: { label: string; x: number; y: number }[];
}

// Item do resumo do fluxo (Fase 23): o passo, o número dele (1, 2, 2.1) e o que vem dentro dele
// (o que o método chama, o corpo do "if", do laço e do "try"), na ordem do código.
export interface OutlineItem {
  // Id do passo na tela (o do passo repetido, quando é o caso) e o do passo original.
  id: string;
  of: string;
  number: string;
  // Quantas chamadas de método separam o passo do método de entrada (0 = o próprio código dele).
  depth: number;
  // Cabeçalho do galho ou da seção que começa neste passo ("Se não", "catch (Exception)").
  head?: string;
  children: OutlineItem[];
}

export interface TimelineRepeat {
  id: string;
  of: string;
}

export interface TimelineDrawing {
  axis: FlowAxis;
  boxes: Map<string, NodeBox>;
  blocks: FlowBlock[];
  heads: TimelineHead[];
  // Passo que aparece de novo (método chamado em dois lugares, ou chamado de volta): a ligação
  // -> o passo repetido, que fica onde a chamada acontece e leva ao original.
  repeats: Map<string, TimelineRepeat>;
  // Número e profundidade de cada passo na tela, e o resumo em árvore (Fase 23).
  numbers: Map<string, string>;
  depths: Map<string, number>;
  outline: OutlineItem[];
}

// Onde a linha de passos pendura os itens do resumo e como numera: o prefixo do passo que a abre,
// a profundidade e o contador, que segue entre os galhos do mesmo passo (then, else; try, catch).
interface Numbering {
  prefix: string;
  depth: number;
  counter: { n: number };
  into: OutlineItem[];
  head?: string;
}

export interface TimelineRoute {
  source: string;
  target: string;
}

// Prefixo do passo repetido.
export const REPEAT_PREFIX = 'again:';

// Identidade de uma ligação que vale no grafo guardado e no desenhado (os índices mudam quando a
// validação engole o desfecho).
export function edgeKey(edge: FlowEdge): string {
  return `${edge.from}>${edge.to}#${edge.order}#${edge.label ?? ''}`;
}

// Posição medida nos sentidos do desenho: u (dos passos) e v (dos galhos).
interface Span {
  u: number;
  v: number;
  len: number;
  thick: number;
}

interface Extent {
  end: number;
  far: number;
}

interface Group {
  label?: string;
  edges: FlowEdge[];
}

// Ligações que saem de cada passo, em ordem e separadas por ramo (o corpo sem rótulo, then,
// else, cada case, try, catch).
function groupsBySource(graph: FlowGraph): Map<string, Group[]> {
  const ids = new Set(graph.nodes.map((n) => n.id));
  const groups = new Map<string, Group[]>();
  graph.edges
    .map((edge, index) => ({ edge, index }))
    .filter(({ edge }) => edge.from !== edge.to && ids.has(edge.to))
    .sort((a, b) => a.edge.order - b.edge.order || a.index - b.index)
    .forEach(({ edge }) => {
      const list = groups.get(edge.from) ?? [];
      const group = list.find((g) => g.label === edge.label);
      if (group) group.edges.push(edge);
      else list.push({ label: edge.label, edges: [edge] });
      groups.set(edge.from, list);
    });
  return groups;
}

// Cabeçalho do galho: só onde ele diz algo. O "then" só quando há "else"; o corpo do método e
// o do laço não têm cabeçalho (o passo já se anuncia).
function headOf(label: string | undefined, groups: Group[]): string | null {
  if (!label || label === BranchLabel.Each) return null;
  if (label === BranchLabel.Then && !groups.some((g) => g.label === BranchLabel.Else)) return null;
  return plainBranch(label);
}

export function timelineDrawing(graph: FlowGraph): TimelineDrawing {
  return flowDrawing(graph, FlowAxis.Down);
}

// Moldura do laço: "Para cada alert em pendingAlerts" -> "↻ Repete para cada alert em pendingAlerts".
function repeatLabel(label: string): string {
  return label.startsWith('Repete') ? `↻ ${label}` : `↻ Repete ${label.charAt(0).toLowerCase()}${label.slice(1)}`;
}

// "extraWidth": largura a mais de um passo (a etiqueta "banco", "mensagem"), que a tela desenha.
export function flowDrawing(graph: FlowGraph, axis: FlowAxis, extraWidth: (node: FlowNode) => number = () => 0): TimelineDrawing {
  const down = axis === FlowAxis.Down;
  const gaps = GAPS[axis];
  const nodes = new Map(graph.nodes.map((n) => [n.id, n]));
  const groups = groupsBySource(graph);
  const spans = new Map<string, Span>();
  const blocks: { id: string; kind: BlockKind; label?: string; u: number; v: number; len: number; thick: number; sections: { label: string; u: number; v: number }[] }[] = [];
  const heads: { u: number; v: number; label: string }[] = [];
  const repeats = new Map<string, TimelineRepeat>();
  const numbers = new Map<string, string>();
  const depths = new Map<string, number>();
  const outline: OutlineItem[] = [];
  const measure = (id: string) => {
    const node = nodes.get(id)!;
    const size = nodeSize(node, extraWidth(node));
    return down ? { len: size.height, thick: size.width } : { len: size.width, thick: size.height };
  };
  // Para baixo, o cabeçalho fica em cima do passo (ocupa uma linha); para a direita, à esquerda
  // dele (ocupa a largura do texto).
  const headLen = (label: string) => (down ? TIMELINE_HEAD : Math.ceil(label.length * HEAD_CHAR_PX + HEAD_CHROME_PX));
  const sectionLen = (label: string) => (down ? SECTION_HEAD : Math.ceil(label.length * HEAD_CHAR_PX + HEAD_CHROME_PX));
  const isLeafDecision = (id: string) => nodes.get(id)?.kind === NodeKind.Decision && !groups.has(id);
  const isBlock = (id: string) => nodes.get(id)?.kind === NodeKind.Try && (groups.get(id) ?? []).some((g) => g.label);

  // Uma linha de passos em v, a partir de u. Os galhos dela ficam numa faixa ao lado; cada galho
  // começa na altura do passo, ou depois do galho anterior se ele ainda ocupa a faixa. Um passo
  // já desenhado em outro lugar aparece de novo aqui, onde a chamada acontece, sem os passos dele.
  const line = (edges: FlowEdge[], v: number, u: number, numbering: Numbering): Extent => {
    if (edges.length === 0) return { end: u - gaps.step, far: v };
    // Numera o passo e o pendura no resumo; o primeiro da linha leva o cabeçalho do galho.
    let head = numbering.head;
    const note = (id: string, of: string): OutlineItem => {
      const number = numbering.prefix ? `${numbering.prefix}.${++numbering.counter.n}` : `${++numbering.counter.n}`;
      const item: OutlineItem = { id, of, number, depth: numbering.depth, children: [], ...(head ? { head } : {}) };
      head = undefined;
      numbering.into.push(item);
      numbers.set(id, number);
      depths.set(id, numbering.depth);
      return item;
    };
    const fresh = new Set(edges.map((e) => e.to).filter((id, i, all) => !spans.has(id) && all.indexOf(id) === i));
    const firsts = new Set<FlowEdge>(edges.filter((e, i) => fresh.has(e.to) && edges.findIndex((o) => o.to === e.to) === i));
    const leaves = [...fresh].filter(isLeafDecision);
    // Validações em sequência com o mesmo tamanho, para o desfecho ficar alinhado (só para baixo:
    // para a direita, a largura é o sentido dos passos).
    const leafThick = down && leaves.length > 1 ? Math.max(...leaves.map((id) => measure(id).thick)) : undefined;
    const thickest = Math.max(...edges.map((e) => (isLeafDecision(e.to) && leafThick && firsts.has(e) ? leafThick : measure(e.to).thick)));
    const lane = { v: v + thickest + gaps.branch, end: -Infinity };
    let next = u;
    let extent: Extent = { end: u, far: v + thickest };
    for (const edge of edges) {
      const id = edge.to;
      if (!firsts.has(edge) || spans.has(id)) {
        const repeat = { id: REPEAT_PREFIX + edgeKey(edge), of: id };
        repeats.set(edgeKey(edge), repeat);
        const size = measure(id);
        spans.set(repeat.id, { u: next, v, ...size });
        note(repeat.id, id);
        extent = { end: next + size.len, far: Math.max(extent.far, v + size.thick) };
        next += size.len + gaps.step;
        continue;
      }
      // O bloco é largo: começa depois dos galhos que já estão na faixa.
      const block = isBlock(id);
      if (block) next = Math.max(next, lane.end + gaps.step);
      // Entrada de outro sistema (Fase 25): a moldura leva o cabeçalho, como a do laço, e o trecho
      // inteiro (entrada e galhos) fica dentro dela.
      const system = nodes.get(id)?.system;
      if (system && system !== nodes.get(edge.from)?.system) {
        next = Math.max(next, lane.end + gaps.step);
        const label = systemLabel(system);
        const frame = next;
        next += headLen(label) + (down ? LOOP_HEAD_GAP : 0);
        // Faixa própria: os galhos da entrada (e os de dentro deles) saem dela, e o fim dela entra na
        // medida da moldura. Na faixa do pai eles passavam do fim do passo e ficavam de fora.
        const own = { v: lane.v, end: -Infinity };
        const inside = step(id, v, next, undefined, own, note(id, id));
        const insideEnd = Math.max(inside.end, own.end);
        blocks.push({ id: `system:${id}`, kind: BlockKind.System, label, u: frame - BLOCK_PAD, v: v - BLOCK_PAD, len: insideEnd - frame + 2 * BLOCK_PAD, thick: inside.far - v + 2 * BLOCK_PAD, sections: [] });
        lane.end = Math.max(lane.end, insideEnd + BLOCK_PAD);
        extent = { end: insideEnd + BLOCK_PAD, far: Math.max(extent.far, inside.far + BLOCK_PAD) };
        next = insideEnd + BLOCK_PAD + gaps.step;
        continue;
      }
      const placed = step(id, v, next, isLeafDecision(id) ? leafThick : undefined, lane, note(id, id));
      if (block) lane.end = Math.max(lane.end, placed.end);
      extent = { end: placed.end, far: Math.max(extent.far, placed.far) };
      next = placed.end + gaps.step;
    }
    return { end: Math.max(extent.end, lane.end), far: extent.far };
  };

  // Desenha o passo e os galhos dele na faixa. Devolve onde o passo termina na linha (o bloco
  // inteiro, para o "try") e até onde vai no sentido dos galhos.
  const step = (id: string, v: number, u: number, thick: number | undefined, lane: { v: number; end: number }, item: OutlineItem): Extent => {
    const node = nodes.get(id)!;
    // O que vem dentro do passo: numerado a partir dele; o corpo de um método fica um nível mais fundo.
    const called = node.kind === NodeKind.Method || node.kind === NodeKind.Entry;
    const counter = { n: 0 };
    const inner = (head?: string): Numbering => ({ prefix: item.number, depth: item.depth + (called ? 1 : 0), counter, into: item.children, head });
    const size = measure(id);
    const span = { u, v, len: size.len, thick: thick ?? size.thick };
    spans.set(id, span);
    let end = u + span.len;
    let far = v + span.thick;
    const own = groups.get(id) ?? [];

    if (isBlock(id)) {
      const sections: { label: string; u: number; v: number }[] = [];
      let next = end + gaps.step;
      own.forEach((group, index) => {
        if (!isSection(group.edges[0], node.kind)) return;
        let label: string | undefined;
        if (index > 0 || group.label !== BranchLabel.Try) {
          label = plainBranch(group.label!);
          sections.push({ label, u: next, v: v + BLOCK_INDENT });
          next += sectionLen(label);
        }
        const placed = line(group.edges, v + BLOCK_INDENT, next, inner(label));
        far = Math.max(far, placed.far);
        end = Math.max(end, placed.end);
        next = placed.end + gaps.step;
      });
      const top = u - BLOCK_PAD;
      end += BLOCK_PAD;
      blocks.push({ id, kind: BlockKind.Try, u: top, v: v - BLOCK_PAD, len: end - top, thick: far - v + 2 * BLOCK_PAD, sections });
      return { end, far: far + BLOCK_PAD };
    }

    for (const group of own) {
      let start = Math.max(u, lane.end + gaps.step);
      // Laço (Fase 23): o que se repete fica numa moldura, com "Repete ..." no cabeçalho.
      if (node.kind === NodeKind.Loop) {
        const label = repeatLabel(node.label);
        const frame = start;
        // Para baixo, o título da moldura ganha folga para o número do primeiro passo, logo abaixo.
        start += headLen(label) + (down ? LOOP_HEAD_GAP : 0);
        const placed = line(group.edges, lane.v, start, inner());
        blocks.push({ id: `${id}:${group.label ?? ''}`, kind: BlockKind.Loop, label, u: frame - BLOCK_PAD, v: lane.v - BLOCK_PAD, len: placed.end - frame + 2 * BLOCK_PAD, thick: placed.far - lane.v + 2 * BLOCK_PAD, sections: [] });
        lane.end = Math.max(lane.end, placed.end + BLOCK_PAD);
        far = Math.max(far, placed.far + BLOCK_PAD);
        continue;
      }
      const head = headOf(group.label, own);
      if (head) {
        heads.push({ u: start, v: lane.v, label: head });
        start += headLen(head);
      }
      const placed = line(group.edges, lane.v, start, inner(head ?? undefined));
      lane.end = Math.max(lane.end, placed.end);
      far = Math.max(far, placed.far);
    }
    return { end, far };
  };

  // A entrada abre a linha principal, logo depois dela.
  let end = 0;
  const entry = nodes.get(graph.entryNodeId);
  if (entry) {
    const size = measure(entry.id);
    spans.set(entry.id, { u: 0, v: 0, ...size });
    const body = (groups.get(entry.id) ?? []).flatMap((g) => g.edges);
    end = Math.max(size.len, line(body, 0, size.len + gaps.step, { prefix: '', depth: 0, counter: { n: 0 }, into: outline }).end);
  }
  // Passos que a entrada não alcança (um passo novo do rascunho ainda solto) ficam no fim.
  for (const node of graph.nodes) {
    if (spans.has(node.id)) continue;
    const size = measure(node.id);
    spans.set(node.id, { u: end + gaps.step * 3, v: 0, ...size });
    end += gaps.step * 3 + size.len;
  }

  // De u/v para a tela.
  const box = (s: Span): NodeBox => (down ? { x: s.v, y: s.u, width: s.thick, height: s.len } : { x: s.u, y: s.v, width: s.len, height: s.thick });
  const point = (u: number, v: number) => (down ? { x: v, y: u } : { x: u, y: v });
  return {
    axis,
    boxes: new Map([...spans].map(([id, s]) => [id, box(s)])),
    // O bloco de fora entra na lista depois dos de dentro (fecha depois deles): do maior para o
    // menor, o de fora fica atrás e não cobre a moldura do laço que está dentro dele.
    blocks: [...blocks].sort((a, b) => b.len * b.thick - a.len * a.thick).map((b) => ({
      id: b.id,
      kind: b.kind,
      ...(b.label ? { label: b.label } : {}),
      ...box(b),
      sections: b.sections.map((s) => ({ label: s.label, ...point(s.u, s.v) })),
    })),
    heads: heads.map((h) => ({ label: h.label, ...point(h.u, h.v) })),
    repeats,
    numbers,
    depths,
    outline,
  };
}

// Por onde cada ligação passa na tela (vale para os dois sentidos). O primeiro passo de um galho
// recebe a seta pela lateral do passo que o abre ("chama" ou "entra"); os seguintes recebem a
// linha do passo anterior ("depois"). A ligação guardada continua a mesma: muda só o desenho.
export function timelineRoutes(graph: FlowGraph, repeats: ReadonlyMap<string, TimelineRepeat> = new Map()): Map<number, TimelineRoute> {
  const kinds = new Map(graph.nodes.map((n) => [n.id, n.kind]));
  const routes = new Map<number, TimelineRoute>();
  const bySource = new Map<string, { edge: FlowEdge; index: number }[]>();
  graph.edges.forEach((edge, index) => {
    if (edge.from === edge.to || !kinds.has(edge.to)) return;
    bySource.set(edge.from, [...(bySource.get(edge.from) ?? []), { edge, index }]);
  });
  for (const [from, list] of bySource) {
    list.sort((a, b) => a.edge.order - b.edge.order || a.index - b.index);
    const fromEntry = from === graph.entryNodeId;
    const previous = new Map<string | undefined, string>();
    for (const { edge, index } of list) {
      // Na entrada, o corpo é a linha principal: um passo só, sem separar por ramo.
      const key = fromEntry ? undefined : edge.label;
      const before = previous.get(key);
      const to = repeats.get(edgeKey(edge))?.id ?? edge.to;
      if (before !== undefined) {
        routes.set(index, { source: Port.Out + before, target: Port.Top + to });
      } else {
        routes.set(
          index,
          fromEntry
            ? { source: Port.Out + from, target: Port.Top + to }
            : kinds.get(from) === NodeKind.Try && isSection(edge, NodeKind.Try)
              ? { source: Port.Out + from, target: Port.In + to }
              : { source: Port.Side + from, target: Port.In + to },
        );
      }
      previous.set(key, to);
    }
  }
  return routes;
}
