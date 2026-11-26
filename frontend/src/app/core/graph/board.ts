import { BoardDetail, BoardLane, BoardLaneUpdate, BoardLayout, BusinessKind, BusinessLayout, SurveyStatus } from '../models';
import { BUSINESS_KIND_ORDER } from './business';

// A posição salva de uma faixa, no formato que a visão de negócio de um levantamento lê (sem o id do levantamento).
export function laneLayout(layout: BoardLayout, laneSurveyId: string): BusinessLayout {
  return {
    steps: layout.steps.filter((s) => s.laneSurveyId === laneSurveyId).map((s) => ({ stepId: s.stepId, x: s.x, y: s.y })),
    edges: layout.edges.filter((e) => e.laneSurveyId === laneSurveyId).map((e) => ({ from: e.from, to: e.to, points: e.points.map((p) => ({ x: p.x, y: p.y })) })),
  };
}

// Faixas na ordem do quadro (o servidor manda ordenado; a tela não confia nisso).
export function orderedLanes(board: BoardDetail): BoardLane[] {
  return [...board.lanes].sort((a, b) => a.order - b.order);
}

// Move a faixa uma casa; nas pontas, não muda nada.
export function moveLane<T>(lanes: readonly T[], index: number, delta: number): T[] {
  const target = index + delta;
  if (index < 0 || index >= lanes.length || target < 0 || target >= lanes.length) return [...lanes];
  const next = [...lanes];
  const [moved] = next.splice(index, 1);
  next.splice(target, 0, moved);
  return next;
}

// A ordem nova e os títulos, no formato da rota de atualização do quadro.
export function laneUpdates(lanes: readonly BoardLane[]): BoardLaneUpdate[] {
  return lanes.map((lane, order) => ({ surveyId: lane.surveyId, order, title: lane.title }));
}

// Os tipos de passo que aparecem no quadro, na ordem fixa da legenda.
export function legendKinds(lanes: readonly BoardLane[]): BusinessKind[] {
  const present = new Set(lanes.flatMap((lane) => lane.flow?.steps.map((s) => s.kind) ?? []));
  return BUSINESS_KIND_ORDER.filter((kind) => present.has(kind));
}

export function laneRunning(lane: BoardLane): boolean {
  return lane.status === SurveyStatus.Pending || lane.status === SurveyStatus.Running;
}

// Texto simples "Termo: significado", uma entrada por linha: devolve as linhas que têm a forma certa.
export function glossaryEntries(text: string): { term: string; meaning: string }[] {
  return text
    .split(/\r?\n/)
    .map((line) => {
      const at = line.indexOf(':');
      return at > 0 ? { term: line.slice(0, at).trim(), meaning: line.slice(at + 1).trim() } : null;
    })
    .filter((entry): entry is { term: string; meaning: string } => entry !== null && entry.term.length > 0 && entry.meaning.length > 0);
}
