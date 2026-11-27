import { DrawioCell } from '../models';

// Juntar e ler o que o usuário mexeu no draw.io (Fase 65). O Rastro não guarda o XML inteiro: ao abrir, gera o .drawio
// do código de novo e aplica por cima as posições dos balões (os que têm rastroId) e as anotações (células sem
// rastroId, desenhadas no editor). Funções puras: o mesmo XML e o mesmo guardado dão sempre o mesmo resultado.

// Ids que o próprio toDrawio gera: o fundo (0 e 1), as molduras (f), as ligações (e) e os balões (n). Célula com outro id
// foi desenhada no editor.
const GENERATED_ID = /^(0|1|[fen]\d+)$/;
export const DRAWIO_MAX_CELLS = 2000;
// O balão leva a referência ao código num span monoespaçado (o mesmo de vertexLabel em drawio.ts).
const REFERENCE_FONT = 'monospace';

export interface DrawioAnnotated {
  cells: readonly DrawioCell[];
  annotations: string | null;
}

// O que se lê do XML do editor: posição e texto de cada balão do código e as anotações.
export interface DrawioChanges {
  cells: DrawioCell[];
  annotations: string | null;
  texts: Map<string, string>;
}

// O que o desenho tinha ao abrir: os balões do código (para saber qual foi apagado), a posição de cada um no editor, o
// texto carregado e, na visão de negócio, a diferença entre a coordenada do .drawio e a da tela (o toDrawio desloca o
// desenho para perto da origem).
export interface DrawioBaseline {
  ids: ReadonlySet<string>;
  positions: ReadonlyMap<string, { x: number; y: number }>;
  texts: ReadonlyMap<string, string>;
  shift: { x: number; y: number } | null;
}

// A linha de base: ids e texto vêm do XML gerado (o que o código diz) e a posição, do XML que foi para o editor (com o
// guardado já aplicado), que é de onde o usuário parte.
export function drawioBaseline(generated: string, loaded: string, shift: { x: number; y: number } | null): DrawioBaseline {
  const from = readDrawioChanges(generated);
  const at = readDrawioChanges(loaded);
  return {
    ids: new Set(from?.texts.keys() ?? []),
    positions: new Map((at?.cells ?? []).map((c) => [c.rastroId, { x: c.x, y: c.y }])),
    texts: from?.texts ?? new Map(),
    shift,
  };
}

// Diferença entre a posição do balão no .drawio gerado e a dele na tela: o toDrawio desloca tudo por igual, então
// qualquer balão serve. Sem balão em comum, sem diferença conhecida.
export function drawioShift(generated: string, boxes: ReadonlyMap<string, { x: number; y: number }>): { x: number; y: number } | null {
  for (const cell of readDrawioChanges(generated)?.cells ?? []) {
    const box = boxes.get(cell.rastroId);
    if (box) return { x: round(cell.x - box.x), y: round(cell.y - box.y) };
  }
  return null;
}

function parseXml(xml: string): Document | null {
  const doc = new DOMParser().parseFromString(xml, 'application/xml');
  return doc.getElementsByTagName('parsererror').length > 0 ? null : doc;
}

const round = (value: number): number => Math.round(value * 100) / 100;

// A raiz das células. O autosave do editor pode vir comprimido (sem <root>): nesse caso não há o que ler.
function rootOf(doc: Document): Element | null {
  return doc.getElementsByTagName('root')[0] ?? null;
}

function geometryOf(cell: Element): Element | null {
  const inner = cell.localName === 'mxCell' ? cell : (Array.from(cell.children).find((c) => c.localName === 'mxCell') ?? null);
  return inner ? (Array.from(inner.children).find((c) => c.localName === 'mxGeometry') ?? null) : null;
}

// Texto do balão: o rótulo é HTML e termina na referência ao código (span monoespaçado); só o texto do passo conta.
export function drawioLabelText(label: string): string {
  const html = new DOMParser().parseFromString(label, 'text/html');
  html.body.querySelectorAll('span').forEach((span) => {
    if ((span.getAttribute('style') ?? '').includes(REFERENCE_FONT)) span.remove();
  });
  html.body.querySelectorAll('br').forEach((br) => br.replaceWith('\n'));
  return (html.body.textContent ?? '').replace(/ /g, ' ').trim();
}

// Aplica o guardado ao XML gerado: troca a geometria dos balões que estão no guardado (o que sumiu do fluxo é
// ignorado) e acrescenta as anotações antes de </root>. XML que não dá para ler volta como veio.
export function applyDrawioLayout(xml: string, saved: DrawioAnnotated): string {
  if (saved.cells.length === 0 && !saved.annotations) return xml;
  const doc = parseXml(xml);
  const root = doc ? rootOf(doc) : null;
  if (!doc || !root) return xml;
  const byId = new Map(saved.cells.map((cell) => [cell.rastroId, cell]));
  for (const child of Array.from(root.children)) {
    const cell = byId.get(child.getAttribute('rastroId') ?? '');
    const geometry = cell ? geometryOf(child) : null;
    if (!cell || !geometry) continue;
    geometry.setAttribute('x', String(round(cell.x)));
    geometry.setAttribute('y', String(round(cell.y)));
    geometry.setAttribute('width', String(round(cell.width)));
    geometry.setAttribute('height', String(round(cell.height)));
  }
  if (saved.annotations) appendAnnotations(doc, root, saved.annotations);
  return new XMLSerializer().serializeToString(doc);
}

function appendAnnotations(doc: Document, root: Element, annotations: string): void {
  const source = parseXml(annotations);
  const from = source ? rootOf(source) : null;
  if (!from) return;
  const taken = new Set(Array.from(root.children).map((c) => c.getAttribute('id')));
  for (const cell of Array.from(from.children)) {
    const id = cell.getAttribute('id');
    if (cell.localName !== 'mxCell' || !id || taken.has(id) || GENERATED_ID.test(id)) continue;
    root.appendChild(doc.importNode(cell, true));
    taken.add(id);
  }
}

// Lê do XML do editor as posições dos balões do código, o texto de cada um e as anotações. Devolve null quando o XML não
// dá para ler (mensagem torta ou comprimida): nada é guardado nesse caso.
export function readDrawioChanges(xml: string): DrawioChanges | null {
  const doc = parseXml(xml);
  const root = doc ? rootOf(doc) : null;
  if (!doc || !root) return null;
  const cells: DrawioCell[] = [];
  const texts = new Map<string, string>();
  const annotations: string[] = [];
  const serializer = new XMLSerializer();
  for (const child of Array.from(root.children)) {
    const id = child.getAttribute('id') ?? '';
    const rastroId = child.getAttribute('rastroId');
    if (rastroId) {
      const geometry = geometryOf(child);
      const box = geometry ? { x: Number(geometry.getAttribute('x')), y: Number(geometry.getAttribute('y')), width: Number(geometry.getAttribute('width')), height: Number(geometry.getAttribute('height')) } : null;
      if (box && Object.values(box).every(Number.isFinite) && box.width > 0 && box.height > 0) {
        cells.push({ rastroId, x: round(box.x), y: round(box.y), width: round(box.width), height: round(box.height) });
      }
      texts.set(rastroId, drawioLabelText(child.getAttribute('label') ?? child.getAttribute('value') ?? ''));
    } else if (id !== '' && !GENERATED_ID.test(id)) {
      annotations.push(serializer.serializeToString(asMxCell(doc, child)));
    }
  }
  return {
    cells: cells.slice(0, DRAWIO_MAX_CELLS),
    annotations: annotations.length > 0 ? `<root>${annotations.join('')}</root>` : null,
    texts,
  };
}

// A anotação guarda só <mxCell>: o objeto com rótulo (UserObject ou object) vira a célula com o valor dele.
function asMxCell(doc: Document, element: Element): Element {
  if (element.localName === 'mxCell') return element;
  const inner = Array.from(element.children).find((c) => c.localName === 'mxCell');
  const cell = doc.createElement('mxCell');
  if (inner) for (const attribute of Array.from(inner.attributes)) cell.setAttribute(attribute.name, attribute.value);
  cell.setAttribute('id', element.getAttribute('id') ?? '');
  cell.setAttribute('value', element.getAttribute('label') ?? element.getAttribute('value') ?? '');
  if (inner) for (const child of Array.from(inner.children)) cell.appendChild(child.cloneNode(true));
  return cell;
}
