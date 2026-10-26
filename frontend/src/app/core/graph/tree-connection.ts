import { EFConnectableSide, IFConnectionBuilder } from '@foblex/flow';

type BuilderRequest = Parameters<IFConnectionBuilder['handle']>[0];
type BuilderResponse = ReturnType<IFConnectionBuilder['handle']>;
interface Point {
  x: number;
  y: number;
}

// Tipo de ligação da árvore (Fase 18), registrado no F_CONNECTION_BUILDERS do canvas.
export const TREE_CONNECTION = 'rastro-tree';
const CORNER = 8;
// O galho vira para baixo (ou para cima) a esta distância do passo de destino.
const BRANCH_TURN = 48;

// Ângulo reto previsível, igual ao do SVG exportado (o "segment" do Foblex escolhe a rota sozinho).
// Tronco: desce da porta de baixo até a altura do passo e vira para ele. Galho: sai pela direita,
// desce (ou sobe) no meio do vão e entra no passo pela esquerda.
export class TreeConnectionBuilder implements IFConnectionBuilder {
  handle({ source, sourceSide, target }: BuilderRequest): BuilderResponse {
    return treePath(treePoints(source, sourceSide, target));
  }
}

export function treePoints(source: Point, sourceSide: EFConnectableSide, target: Point): Point[] {
  if (sourceSide === EFConnectableSide.BOTTOM) return dedupe([source, { x: source.x, y: target.y }, target]);
  const turn = target.x > source.x ? Math.max(source.x + CORNER * 2, target.x - BRANCH_TURN) : source.x + CORNER * 2;
  return dedupe([source, { x: turn, y: source.y }, { x: turn, y: target.y }, target]);
}

export function treePath(points: Point[]): BuilderResponse {
  let d = `M ${points[0].x} ${points[0].y}`;
  for (let i = 1; i < points.length - 1; i++) {
    const [before, corner, after] = [points[i - 1], points[i], points[i + 1]];
    const inR = Math.min(CORNER, distance(before, corner) / 2);
    const outR = Math.min(CORNER, distance(corner, after) / 2);
    const enter = toward(corner, before, inR);
    const leave = toward(corner, after, outR);
    d += ` L ${enter.x} ${enter.y} Q ${corner.x} ${corner.y} ${leave.x} ${leave.y}`;
  }
  const last = points[points.length - 1];
  d += ` L ${last.x} ${last.y}`;
  const candidates = points.slice(1).map((p, i) => ({ x: (points[i].x + p.x) / 2, y: (points[i].y + p.y) / 2 }));
  return {
    path: d,
    points,
    candidates,
    secondPoint: points[1] ?? last,
    penultimatePoint: points[points.length - 2] ?? points[0],
  };
}

function dedupe(points: Point[]): Point[] {
  return points.filter((p, i) => i === 0 || p.x !== points[i - 1].x || p.y !== points[i - 1].y);
}

function distance(a: Point, b: Point): number {
  return Math.hypot(a.x - b.x, a.y - b.y);
}

// Ponto a "length" de "from" em direção a "to".
function toward(from: Point, to: Point, length: number): Point {
  const total = distance(from, to);
  if (total === 0) return from;
  return { x: from.x + ((to.x - from.x) * length) / total, y: from.y + ((to.y - from.y) * length) / total };
}
