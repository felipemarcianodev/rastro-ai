import { BoardStepPosition } from '../models';
import { snapToGrid } from './business-layout';
import { readDrawioBalloons, readDrawioChanges } from './drawio-merge';

// O quadro de fluxos aberto no draw.io (Fase 68). O id do passo é por levantamento: o balão leva também a faixa
// (rastroLane), e tudo aqui é indexado pelo par faixa e passo. Funções puras.

export const boardKey = (lane: string, stepId: string): string => `${lane}\u0001${stepId}`;

type Point = { x: number; y: number };

// O que o editor tinha ao abrir: a diferença entre a coordenada do .drawio e a da faixa (por faixa), a posição e o texto
// de cada balão e as anotações guardadas. A página atualiza as tabelas conforme grava.
export interface BoardDrawioBase {
  shifts: Map<string, Point>;
  positions: Map<string, Point>;
  texts: Map<string, string>;
  annotations: string | null;
}

// O que mudou no editor desde a base.
export interface BoardDrawioChanges {
  moved: { step: BoardStepPosition; at: Point }[];
  labels: { lane: string; stepId: string; text: string }[];
  annotations: string | null;
  annotationsChanged: boolean;
}

// A diferença por faixa: o toDrawio põe cada faixa ao lado da outra e leva tudo para perto da origem, então a diferença
// entre o balão no XML gerado e o mesmo na faixa vale para a faixa inteira. Faixa sem balão em comum fica sem diferença.
export function boardShifts(generated: string, boxes: ReadonlyMap<string, ReadonlyMap<string, Point>>): Map<string, Point> {
  const shifts = new Map<string, Point>();
  for (const balloon of readDrawioBalloons(generated) ?? []) {
    const lane = balloon.lane;
    const box = lane ? boxes.get(lane)?.get(balloon.rastroId) : undefined;
    if (lane && box && !shifts.has(lane)) shifts.set(lane, { x: Math.round((balloon.x - box.x) * 100) / 100, y: Math.round((balloon.y - box.y) * 100) / 100 });
  }
  return shifts;
}

// A base: texto do XML gerado (o que o código e o usuário disseram), posição do XML que foi para o editor.
export function boardDrawioBase(generated: string, loaded: string, boxes: ReadonlyMap<string, ReadonlyMap<string, Point>>): BoardDrawioBase {
  const positions = new Map<string, Point>();
  for (const b of readDrawioBalloons(loaded) ?? []) if (b.lane) positions.set(boardKey(b.lane, b.rastroId), { x: b.x, y: b.y });
  const texts = new Map<string, string>();
  for (const b of readDrawioBalloons(generated) ?? []) if (b.lane) texts.set(boardKey(b.lane, b.rastroId), b.text);
  return { shifts: boardShifts(generated, boxes), positions, texts, annotations: readDrawioChanges(loaded)?.annotations ?? null };
}

// Compara o XML do editor com a base. XML que não dá para ler (comprimido) não muda nada.
export function boardDrawioChanges(xml: string, base: BoardDrawioBase): BoardDrawioChanges | null {
  const balloons = readDrawioBalloons(xml);
  const read = readDrawioChanges(xml);
  if (!balloons || !read) return null;
  const moved: BoardDrawioChanges['moved'] = [];
  const labels: BoardDrawioChanges['labels'] = [];
  for (const b of balloons) {
    if (!b.lane) continue;
    const key = boardKey(b.lane, b.rastroId);
    const known = base.positions.get(key);
    const shift = base.shifts.get(b.lane);
    if (known && shift && (known.x !== b.x || known.y !== b.y)) {
      moved.push({ step: { laneSurveyId: b.lane, stepId: b.rastroId, x: snapToGrid(b.x - shift.x), y: snapToGrid(b.y - shift.y) }, at: { x: b.x, y: b.y } });
    }
    if (b.text !== '' && base.texts.has(key) && base.texts.get(key) !== b.text) labels.push({ lane: b.lane, stepId: b.rastroId, text: b.text });
  }
  return { moved, labels, annotations: read.annotations, annotationsChanged: read.annotations !== base.annotations };
}
