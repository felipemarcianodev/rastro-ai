import { ChangeKind, FlowEdge, FlowGraph, NodeKind } from '../models';
import { addStep, connect, isForeign, reassign, removeEdge, removeStep, sameEdge } from './draft-editor';
import { drawioLabelText } from './drawio-merge';
import { edgeRoutes } from './export';

// Editar o fluxo pelo draw.io (Fase 67). O que o usuário faz no editor (balão novo, balão apagado, ligação nova, ligação
// apagada ou movida) vira a mesma chamada que a tela de edição faz sobre o rascunho (draft-editor). Função pura e
// determinística, sem IA: o mesmo rascunho e os mesmos dois XML dão sempre o mesmo resultado.

// Ids que o próprio toDrawio gera para o fundo e para as molduras: não são passos nem ligações.
const SKIPPED_ID = /^(0|1|f\d+)$/;
// Ligação gerada pelo toDrawio; as desenhadas no editor ganham outro id.
const GENERATED_EDGE = /^e\d+$/;
const DECISION_STYLE = 'rhombus';
// Seta do editor já tratada (ligada ou recusada), guardada no `known` com prefixo próprio.
const EDGE_PREFIX = 'edge:';
const REFUSED = 'recusada';
const edgeKey = (cellId: string): string => `${EDGE_PREFIX}${cellId}`;

export interface DrawioDraftResult {
  draft: FlowGraph;
  // Célula do draw.io (id) para o id do passo novo que ela virou: o próximo autosave a reconhece.
  known: Map<string, string>;
  // Ids dos passos novos, na ordem em que foram criados.
  created: string[];
  // Pedidos recusados, no total e pelo motivo (o passo de entrada não sai; passo de outro sistema não se edita).
  refused: number;
  entryRefused: number;
  foreignRefused: number;
}

interface Cell {
  id: string;
  vertex: boolean;
  edge: boolean;
  rastroId: string | null;
  // Balão repetido (↻): tem o nó por trás, mas não é um passo do rascunho.
  repeat: boolean;
  style: string;
  label: string;
  source: string | null;
  target: string | null;
}

function readCells(xml: string): Map<string, Cell> | null {
  const doc = new DOMParser().parseFromString(xml, 'application/xml');
  const root = doc.getElementsByTagName('parsererror').length > 0 ? null : (doc.getElementsByTagName('root')[0] ?? null);
  if (!root) return null;
  const cells = new Map<string, Cell>();
  for (const child of Array.from(root.children)) {
    const id = child.getAttribute('id') ?? '';
    if (id === '' || SKIPPED_ID.test(id)) continue;
    const inner = child.localName === 'mxCell' ? child : (Array.from(child.children).find((c) => c.localName === 'mxCell') ?? null);
    if (!inner) continue;
    const rastroId = child.getAttribute('rastroId');
    cells.set(id, {
      id,
      vertex: inner.getAttribute('vertex') === '1',
      edge: inner.getAttribute('edge') === '1',
      rastroId: rastroId || null,
      repeat: !rastroId && !!child.getAttribute('rastroNode'),
      style: inner.getAttribute('style') ?? '',
      label: drawioLabelText(child.getAttribute('label') ?? child.getAttribute('value') ?? inner.getAttribute('value') ?? '').replace(/\s+/g, ' ').trim(),
      source: inner.getAttribute('source'),
      target: inner.getAttribute('target'),
    });
  }
  return cells;
}

// Aplica ao rascunho o que mudou entre o XML que o editor tinha (loadedXml) e o atual (currentXml). Quem chama passa,
// a cada autosave, o XML do autosave anterior como "carregado" e o `known` devolvido da vez anterior.
export function drawioToDraft(draft: FlowGraph, loadedXml: string, currentXml: string, known: Map<string, string>): DrawioDraftResult {
  const nextKnown = new Map(known);
  const result: DrawioDraftResult = { draft, known: nextKnown, created: [], refused: 0, entryRefused: 0, foreignRefused: 0 };
  const before = readCells(loadedXml);
  const after = readCells(currentXml);
  if (!before || !after) return result;

  // Balão copiado (mesmo rastroId, id novo) não é o passo original: só vale o rastroId de célula que já estava lá.
  const nodeIdOf = (cells: Map<string, Cell>, cellId: string | null): string | undefined => {
    const cell = cellId ? cells.get(cellId) : undefined;
    if (!cell || !cell.vertex || cell.repeat) return undefined;
    if (nextKnown.has(cell.id)) return nextKnown.get(cell.id);
    return cell.rastroId && before.has(cell.id) ? cell.rastroId : undefined;
  };
  const refuse = (reason: 'entry' | 'foreign'): void => {
    result.refused++;
    if (reason === 'entry') result.entryRefused++;
    else result.foreignRefused++;
  };
  let current = draft;
  // A linha desenhada nem sempre sai do passo que chama: o passo seguinte recebe a linha do anterior (timelineRoutes).
  // Para achar a ligação do rascunho que uma linha do XML representa, usa-se a mesma rota da tela.
  const routed = new Map<string, FlowEdge[]>();
  for (const route of edgeRoutes(draft)) {
    const key = routeKey(route.source, route.target);
    routed.set(key, [...(routed.get(key) ?? []), draft.edges[route.index]]);
  }

  // 1. Balões que sumiram: o passo sai do fluxo (o de entrada e o de outro sistema não saem).
  const gone = new Set<string>();
  const forgotten: string[] = [];
  for (const cell of before.values()) {
    if (!cell.vertex || after.has(cell.id)) continue;
    const nodeId = nodeIdOf(before, cell.id);
    if (!nodeId) continue;
    gone.add(nodeId);
    if (nextKnown.has(cell.id)) forgotten.push(cell.id);
    const node = current.nodes.find((n) => n.id === nodeId);
    if (!node) continue;
    if (nodeId === current.entryNodeId) refuse('entry');
    else if (isForeign(current, nodeId)) refuse('foreign');
    else if (node.change !== ChangeKind.Removed) current = removeStep(current, nodeId);
  }

  // 2. Balões novos: cada um vira um passo novo, sem o que ele deve fazer (o painel do passo pede). No editor a caixa
  // nasce vazia e ganha o texto num autosave seguinte: por isso vale a célula sem rastroId que ainda não virou passo
  // (`known`), e não "a que não estava no XML anterior" (visto no navegador: o passo novo nunca entrava).
  for (const cell of after.values()) {
    if (!cell.vertex || cell.rastroId || cell.repeat || nextKnown.has(cell.id) || cell.label === '') continue;
    const kind = cell.style.split(';').includes(DECISION_STYLE) ? NodeKind.Decision : NodeKind.Method;
    const added = addStep(current, cell.label, kind, '');
    current = added.draft;
    nextKnown.set(cell.id, added.id);
    result.created.push(added.id);
  }

  // 3. Ligações que sumiram ou mudaram de ponta.
  for (const cell of before.values()) {
    if (!cell.edge) continue;
    const from = nodeIdOf(before, cell.source);
    const to = nodeIdOf(before, cell.target);
    if (!from || !to || gone.has(from) || gone.has(to)) continue;
    const old = findEdge(current, routed, from, to, cell.label);
    const next = after.get(cell.id);
    const nextFrom = next?.edge ? nodeIdOf(after, next.source) : undefined;
    const nextTo = next?.edge ? nodeIdOf(after, next.target) : undefined;
    if (next && nextFrom && nextTo && (nextFrom === from && nextTo === to)) continue;
    if (touchesForeign(current, [from, to, nextFrom, nextTo, old?.from, old?.to])) {
      refuse('foreign');
      continue;
    }
    if (next && nextFrom && nextTo) {
      // Mudou de ponta: sai de onde estava e entra no lugar novo (o mesmo que arrastar a ligação na tela).
      // Só a ponta que o usuário moveu muda: a outra segue como a ligação do rascunho a tem (a linha desenhada pode sair
      // do passo anterior, não de quem chama).
      if (old) {
        current = reassign(current, old, { from: nextFrom !== from ? nextFrom : undefined, to: nextTo !== to ? nextTo : undefined });
      } else current = connect(current, nextFrom, nextTo, label(next));
    } else if (old) {
      current = removeEdge(current, old);
    }
  }

  // 4. Ligações novas entre dois balões conhecidos. A seta desenhada no editor (id que não é o "eN" gerado) conta até ser
  // ligada uma vez: ela pode ser desenhada antes de a caixa da ponta ganhar o texto e virar passo.
  for (const cell of after.values()) {
    if (!cell.edge || GENERATED_EDGE.test(cell.id) || nextKnown.has(edgeKey(cell.id))) continue;
    const from = nodeIdOf(after, cell.source);
    const to = nodeIdOf(after, cell.target);
    if (!from || !to || from === to) continue;
    if (touchesForeign(current, [from, to])) {
      refuse('foreign');
      nextKnown.set(edgeKey(cell.id), REFUSED);
      continue;
    }
    current = connect(current, from, to, label(cell));
    nextKnown.set(edgeKey(cell.id), from);
  }

  for (const id of forgotten) nextKnown.delete(id);
  // Seta do editor que sumiu: a marca de "já ligada" sai junto (a ligação em si sai pelo passo 3, quando é gerada, ou
  // pelo passo removido).
  for (const key of [...nextKnown.keys()]) {
    if (key.startsWith(EDGE_PREFIX) && !after.has(key.slice(EDGE_PREFIX.length))) nextKnown.delete(key);
  }
  result.draft = current;
  return result;
}

const label = (cell: Cell): string | undefined => cell.label || undefined;

function touchesForeign(draft: FlowGraph, ids: (string | undefined)[]): boolean {
  return ids.some((id) => isForeign(draft, id));
}

const routeKey = (from: string, to: string): string => `${from} ${to}`;

// A ligação do rascunho que a linha representa: a de mesma rota na tela e, se não houver, a de mesma origem e destino
// (linha desenhada no editor). O ramo desempata quando há mais de uma.
function findEdge(draft: FlowGraph, routed: Map<string, FlowEdge[]>, from: string, to: string, text: string): FlowEdge | undefined {
  const candidates = [...(routed.get(routeKey(from, to)) ?? []), ...draft.edges.filter((e) => e.from === from && e.to === to)];
  const live = candidates.flatMap((candidate) => {
    const found = draft.edges.find((e) => sameEdge(e, candidate));
    return found && found.change !== ChangeKind.Removed ? [found] : [];
  });
  return live.find((e) => (e.label ?? '') === text) ?? live[0];
}
