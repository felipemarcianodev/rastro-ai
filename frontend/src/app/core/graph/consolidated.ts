import { FlowGraph, FlowNode, NodeKind } from '../models';
import { httpLabel, mermaidText } from './export';
import { NodeEffect, effectOf, humanName, splitLabel } from './reading';
import { plainLabel } from './plain';
import { OutlineItem, timelineDrawing } from './timeline-layout';
import { withoutFrameworkCalls } from './visibility';

// Visão consolidada (Fase 27): o fluxo inteiro num diagrama Mermaid só, na ordem de leitura e
// com o texto em português. Puro e determinístico (sem IA): o mesmo grafo dá o mesmo texto, e o
// id de cada passo vem do id do nó, não da posição, para a edição do dono sobreviver a um novo
// levantamento (mergeDiagram).

// Classes de tipo declaradas no texto gerado (cores neutras, sem verde). O dono muda pela tela.
export enum DiagramClass {
  Entry = 0,
  Decision = 1,
  External = 2,
  Error = 3,
  Unresolved = 4,
}

export const DIAGRAM_CLASS_NAME: Record<DiagramClass, string> = {
  [DiagramClass.Entry]: 'entry',
  [DiagramClass.Decision]: 'decision',
  [DiagramClass.External]: 'external',
  [DiagramClass.Error]: 'error',
  [DiagramClass.Unresolved]: 'unresolved',
};

const CLASS_DEF: Record<DiagramClass, string> = {
  [DiagramClass.Entry]: 'fill:#1d2a2e,stroke:#1d2a2e,color:#ffffff',
  [DiagramClass.Decision]: 'fill:#e8eef5,stroke:#2f5d8a,color:#1d2a2e',
  [DiagramClass.External]: 'fill:#eceeed,stroke:#5c6a6e,color:#1d2a2e',
  [DiagramClass.Error]: 'fill:#fbe9e7,stroke:#b42318,color:#1d2a2e',
  [DiagramClass.Unresolved]: 'fill:#fff4e0,stroke:#a25a00,color:#1d2a2e,stroke-dasharray:3 3',
};

const KIND_CLASS: Partial<Record<NodeKind, DiagramClass>> = {
  [NodeKind.Entry]: DiagramClass.Entry,
  [NodeKind.Decision]: DiagramClass.Decision,
  [NodeKind.Loop]: DiagramClass.Decision,
  [NodeKind.External]: DiagramClass.External,
  [NodeKind.Throw]: DiagramClass.Error,
  [NodeKind.Unresolved]: DiagramClass.Unresolved,
};

const CLASS_ORDER = [DiagramClass.Entry, DiagramClass.Decision, DiagramClass.External, DiagramClass.Error, DiagramClass.Unresolved];

const LABEL_THEN = 'Se sim';
const LABEL_ELSE = 'Se não';
const LABEL_OTHER_CASES = 'Nos outros casos';

// Hash FNV-1a de 32 bits em base 36: o id de um passo no Mermaid ("s" + hash).
export function stepId(nodeId: string): string {
  let hash = 0x811c9dc5;
  for (let i = 0; i < nodeId.length; i++) {
    hash ^= nodeId.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  return `s${(hash >>> 0).toString(36)}`;
}

// Passo que fica no diagrama e as ligações que saem dele ainda sem destino.
interface Tail {
  from: string;
  label?: string;
}

interface Step {
  id: string;
  shape: string;
  cls?: DiagramClass;
  // Sistema de onde o passo vem ('' = o primeiro, do repositório do levantamento).
  system: string;
}

interface Group {
  label: string;
  head?: string;
  items: OutlineItem[];
}

// "firstSystem": nome do repositório do levantamento, o título do primeiro subgraph quando o fluxo
// passa por mais de um sistema (Fase 25).
export function consolidatedMermaid(graph: FlowGraph, firstSystem?: string): string {
  const shown = withoutFrameworkCalls(graph);
  const nodes = new Map(shown.nodes.map((n) => [n.id, n]));
  const entry = nodes.get(shown.entryNodeId);
  if (!entry) return 'flowchart TD\n';

  const steps: Step[] = [];
  const links: string[] = [];
  const seen = new Set<string>();
  let repeats = 0;
  // Os "return" de cada método aberto, do de entrada (o fim do fluxo) ao mais fundo.
  const returns: Tail[][] = [[]];

  const add = (id: string, node: FlowNode, repeat: boolean): void => {
    const text = mermaidText(`${repeat ? '↻ ' : ''}${stepText(node)}`);
    steps.push({ id, shape: shapeOf(node, `"${text}"`), cls: KIND_CLASS[node.kind], system: node.system ?? '' });
  };
  const connect = (tails: Tail[], to: string): void => {
    for (const tail of tails) {
      const line = tail.label ? `  ${tail.from} -- "${mermaidText(tail.label)}" --> ${to}` : `  ${tail.from} --> ${to}`;
      if (seen.has(line)) continue;
      seen.add(line);
      links.push(line);
    }
  };

  // Ligação HTTP que chega em cada passo (Fase 25): vira o rótulo "VERBO rota" da seta.
  const httpInto = new Map<string, string>();
  for (const edge of shown.edges) {
    const label = httpLabel(edge);
    if (label && !httpInto.has(edge.to)) httpInto.set(edge.to, label);
  }

  // Um passo do resumo: devolve as ligações que seguem dele para o passo seguinte.
  const place = (item: OutlineItem, incoming: Tail[]): Tail[] => {
    const node = nodes.get(item.of);
    if (!node || node.kind === NodeKind.Jump) return incoming;
    const repeat = item.id !== item.of;
    const http = httpInto.get(item.of);
    const pending = http ? incoming.map((t) => ({ ...t, label: t.label ? `${t.label}: ${http}` : http })) : incoming;
    const children = repeat ? [] : item.children;

    if (node.kind === NodeKind.Try) {
      if (repeat) return pending;
      // O try não vira passo: o corpo segue em sequência e cada catch/finally sai do mesmo ponto.
      const [body, ...sections] = groups(children, false);
      const tails = body ? [...emit(body.items, pending)] : [...pending];
      for (const section of sections) {
        tails.push(...emit(section.items, pending.map((t) => ({ from: t.from, label: section.head }))));
      }
      return tails;
    }
    // Método de passagem (sem regra, com filhos): some, e os filhos entram no lugar dele. O
    // "return" dentro dele encerra só ele: o fluxo segue do return para o passo seguinte de quem chamou.
    // A entrada de outro sistema (a que a chamada HTTP alcança) nunca some: é onde o fluxo
    // atravessa para a outra API, e sem ela a seta "VERBO rota" cairia no meio do outro sistema.
    const crossing = !!http && node.kind === NodeKind.Method;
    if (node.kind === NodeKind.Method && !node.rule && children.length > 0 && !crossing) {
      returns.push([]);
      const tails = emit(children, pending);
      return [...tails, ...returns.pop()!];
    }

    const id = repeat ? `${stepId(item.of)}_r${++repeats}` : stepId(item.of);
    add(id, node, repeat);
    connect(pending, id);
    if (crossing && !node.rule && children.length > 0) {
      returns.push([]);
      const tails = emit(children, [{ from: id }]);
      return [...tails, ...returns.pop()!];
    }
    if (node.kind === NodeKind.Return) {
      returns[returns.length - 1].push({ from: id });
      return [];
    }
    if (node.kind === NodeKind.Throw) return [];
    // Método com regra: um passo só, o texto da regra já resume o que ele faz.
    if (node.kind === NodeKind.Method || children.length === 0) return [{ from: id }];

    if (node.kind === NodeKind.Loop) {
      const tails = emit(children, [{ from: id }]);
      return tails.length > 0 ? tails : [{ from: id }];
    }
    if (node.kind === NodeKind.Decision) {
      const branches = groups(children, true);
      const tails = branches.flatMap((group) => emit(group.items, [{ from: id, label: group.label }]));
      // Sem "Se não" (ou "Nos outros casos"), a decisão também segue direto para o passo seguinte.
      if (!branches.some((g) => g.label === LABEL_ELSE || g.label === LABEL_OTHER_CASES)) {
        tails.push({ from: id, label: branches.some((g) => g.label !== LABEL_THEN) ? LABEL_OTHER_CASES : LABEL_ELSE });
      }
      return tails;
    }
    return [{ from: id }];
  };

  const emit = (items: OutlineItem[], pending: Tail[]): Tail[] => items.reduce((tails, item) => place(item, tails), pending);

  const entryId = stepId(entry.id);
  add(entryId, entry, false);
  emit(timelineDrawing(shown).outline, [{ from: entryId }]);

  const lines = ['flowchart TD'];
  const systems = [...new Set(steps.map((s) => s.system))].sort((a, b) => (a === '' ? -1 : b === '' ? 1 : 0));
  if (systems.length > 1) {
    // Um subgraph por sistema; ids sys0, sys1... e título entre aspas (o nome pode ter ponto e hífen).
    systems.forEach((system, index) => {
      lines.push(`  subgraph sys${index}["${mermaidText(system === '' ? (firstSystem ?? 'Primeiro sistema') : system)}"]`);
      for (const step of steps.filter((s) => s.system === system)) lines.push(`    ${step.id}${step.shape}`);
      lines.push('  end');
    });
  } else {
    for (const step of steps) lines.push(`  ${step.id}${step.shape}`);
  }
  lines.push(...links);
  for (const cls of CLASS_ORDER) lines.push(`  classDef ${DIAGRAM_CLASS_NAME[cls]} ${CLASS_DEF[cls]}`);
  for (const cls of CLASS_ORDER) {
    const ids = steps.filter((s) => s.cls === cls).map((s) => s.id);
    if (ids.length > 0) lines.push(`  class ${ids.join(',')} ${DIAGRAM_CLASS_NAME[cls]}`);
  }
  return lines.join('\n') + '\n';
}

// Os filhos de um passo viram grupos: um por cabeçalho ("Se sim", "Se não", "Caso X", "catch ...").
// Na decisão, o primeiro grupo sem cabeçalho é o "Se sim"; no try, é o corpo.
function groups(children: OutlineItem[], decision: boolean): Group[] {
  const result: Group[] = [];
  for (const child of children) {
    const current = result[result.length - 1];
    if (!current || child.head) {
      result.push({ label: child.head ?? (decision ? LABEL_THEN : ''), head: child.head, items: [child] });
    } else {
      current.items.push(child);
    }
  }
  return result;
}

// Texto do passo: a regra do dono; senão o comando em português; senão o nome legível do método.
function stepText(node: FlowNode): string {
  if (node.rule?.text) return node.rule.text;
  const plain = plainLabel(node);
  if (plain) return plain;
  return node.kind === NodeKind.Method || node.kind === NodeKind.Entry ? humanName(splitLabel(node).name) : node.label;
}

function shapeOf(node: FlowNode, text: string): string {
  switch (node.kind) {
    case NodeKind.Decision:
      return `{${text}}`;
    case NodeKind.Loop:
      return `{{${text}}}`;
    case NodeKind.External:
      return effectOf(node) === NodeEffect.Database ? `[(${text})]` : `[[${text}]]`;
    case NodeKind.Unresolved:
      return `[/${text}/]`;
    case NodeKind.Throw:
      return `>${text}]`;
    case NodeKind.Entry:
    case NodeKind.Return:
      return `([${text}])`;
    default:
      return `[${text}]`;
  }
}

// ---------------------------------------------------------------------------------------------
// Edição do dono: cor por passo e por tipo, e o refazer que não perde a edição.

export interface DiagramStyle {
  fill?: string;
  stroke?: string;
  color?: string;
}

const STYLE_KEYS: (keyof DiagramStyle)[] = ['fill', 'stroke', 'color'];
// Só cor em hexadecimal entra no texto: nada que quebre a linha ou injete outro comando.
const HEX = /^#[0-9a-fA-F]{3,8}$/;
const IDENT = /^[\w-]+$/;

const NODE_LINE = /^(\s*)([\w-]+)\s*(\(\[|\[\(|\[\[|\[\/|\{\{|\(\(|\{|>|\[|\()\s*"(.*)"\s*(\]\)|\)\]|\]\]|\/\]|\}\}|\)\)|\}|\]|\))\s*;?\s*$/;
const CLASSDEF_LINE = /^\s*classDef\s+([\w-]+)\s+(.*?)\s*;?\s*$/;
const STYLE_LINE = /^\s*style\s+([\w-]+)\s+(.*?)\s*;?\s*$/;
const CLASS_LINE = /^\s*class\s+([\w,-]+)\s+([\w-]+)\s*;?\s*$/;

export function setNodeStyle(text: string, id: string, style: DiagramStyle): string {
  if (!IDENT.test(id)) return text;
  return replaceLine(text, (line) => STYLE_LINE.exec(line)?.[1] === id, (props) => `  style ${id} ${props}`, style, (line) => STYLE_LINE.exec(line)?.[2]);
}

export function setKindStyle(text: string, className: string, style: DiagramStyle): string {
  if (!IDENT.test(className)) return text;
  return replaceLine(text, (line) => CLASSDEF_LINE.exec(line)?.[1] === className, (props) => `  classDef ${className} ${props}`, style, (line) => CLASSDEF_LINE.exec(line)?.[2]);
}

// Troca a linha existente (mantendo as propriedades que a tela não mexe, como o tracejado) ou
// acrescenta uma no fim.
function replaceLine(
  text: string,
  matches: (line: string) => boolean,
  build: (props: string) => string,
  style: DiagramStyle,
  currentProps: (line: string) => string | undefined,
): string {
  const lines = text.split('\n');
  const index = lines.findIndex(matches);
  const props = parseProps(index >= 0 ? currentProps(lines[index]) ?? '' : '');
  for (const key of STYLE_KEYS) {
    const value = style[key];
    if (value && HEX.test(value)) props.set(key, value);
  }
  // Nada válido para escrever: o texto fica como estava (uma linha "style x" vazia quebraria o Mermaid).
  if (props.size === 0) return text;
  const line = build([...props].map(([k, v]) => `${k}:${v}`).join(','));
  if (index >= 0) {
    lines[index] = line;
    return lines.join('\n');
  }
  const trailing = text.endsWith('\n');
  const body = trailing ? lines.slice(0, -1) : lines;
  return [...body, line].join('\n') + (trailing ? '\n' : '');
}

function parseProps(props: string): Map<string, string> {
  const map = new Map<string, string>();
  // "stroke-dasharray:3 3" tem espaço, mas nunca vírgula: separa só pela vírgula.
  for (const part of props.split(',')) {
    const colon = part.indexOf(':');
    if (colon > 0) map.set(part.slice(0, colon).trim(), part.slice(colon + 1).trim());
  }
  return map;
}

// A classe de tipo do passo no texto ("class a,b decision"), para o botão "Todos do tipo".
export function kindClassOf(text: string, id: string): string | undefined {
  for (const line of text.split('\n')) {
    const match = CLASS_LINE.exec(line);
    if (match && match[1].split(',').includes(id)) return match[2];
  }
  return undefined;
}

// O Mermaid põe "flowchart-<id>-<n>" no id do g.node (com um prefixo da renderização).
export function nodeIdFromSvg(elementId: string): string | null {
  return /flowchart-(.+)-\d+$/.exec(elementId)?.[1] ?? null;
}

// Refaz o diagrama a partir do grafo novo sem perder a edição: as classDef do dono, as cores e as
// classes de passos que ainda existem e o texto que o dono deu a eles. "dropped" conta os passos
// com edição de cor que não existem mais no código. Com "baseline" (o texto que o gerador dava para o
// grafo de quando o anterior foi editado), só o texto que o dono mudou de fato é mantido: o passo que
// ele não tocou recebe o texto novo do código.
export function mergeDiagram(previous: string, generated: string, baseline?: string): { text: string; dropped: number } {
  const old = parse(previous);
  const fresh = parse(generated);
  const original = baseline === undefined ? null : parse(baseline);
  // Sem baseline, todo texto anterior vale. Com ele, vale o que difere do original do mesmo passo
  // (passo ausente do baseline é do dono).
  const ownText = (id: string): string | undefined => {
    const text = old.texts.get(id);
    if (text === undefined || !original) return text;
    const base = original.texts.get(id);
    return base !== undefined && base === text ? undefined : text;
  };

  const lines = generated.split('\n').map((line) => {
    const node = NODE_LINE.exec(line);
    const edited = node && ownText(node[2]);
    if (node && edited !== undefined) return `${node[1]}${node[2]}${node[3]}"${edited}"${node[5]}`;
    const def = CLASSDEF_LINE.exec(line);
    const own = def && old.classDefs.get(def[1]);
    return own ? `  classDef ${def[1]} ${own}` : line;
  });
  // Termina com uma quebra de linha só, como o gerado.
  while (lines.length > 0 && lines[lines.length - 1] === '') lines.pop();

  for (const [name, props] of old.classDefs) if (!fresh.classDefs.has(name)) lines.push(`  classDef ${name} ${props}`);
  for (const [id, props] of old.styles) if (fresh.texts.has(id)) lines.push(`  style ${id} ${props}`);
  // Classes do dono só dos passos que ainda existem, sem repetir o que o gerado já atribuiu.
  const byClass = new Map<string, string[]>();
  for (const [id, classes] of old.classes) {
    if (!fresh.texts.has(id)) continue;
    for (const name of classes) {
      if (fresh.classes.get(id)?.has(name)) continue;
      byClass.set(name, [...(byClass.get(name) ?? []), id]);
    }
  }
  for (const [name, ids] of byClass) lines.push(`  class ${ids.join(',')} ${name}`);

  const gone = new Set<string>();
  for (const id of old.styles.keys()) if (!fresh.texts.has(id)) gone.add(id);
  // Classe que o dono criou (fora das cinco do gerado) também conta como edição.
  const defaults = new Set(Object.values(DIAGRAM_CLASS_NAME));
  for (const [id, classes] of old.classes) {
    if (!fresh.texts.has(id) && [...classes].some((name) => !defaults.has(name))) gone.add(id);
  }
  return { text: lines.join('\n') + '\n', dropped: gone.size };
}

interface ParsedDiagram {
  texts: Map<string, string>;
  classDefs: Map<string, string>;
  styles: Map<string, string>;
  classes: Map<string, Set<string>>;
}

function parse(text: string): ParsedDiagram {
  const result: ParsedDiagram = { texts: new Map(), classDefs: new Map(), styles: new Map(), classes: new Map() };
  for (const line of text.split('\n')) {
    const node = NODE_LINE.exec(line);
    if (node) {
      result.texts.set(node[2], node[4]);
      continue;
    }
    const def = CLASSDEF_LINE.exec(line);
    if (def) {
      result.classDefs.set(def[1], def[2]);
      continue;
    }
    const style = STYLE_LINE.exec(line);
    if (style) {
      result.styles.set(style[1], style[2]);
      continue;
    }
    const assignment = CLASS_LINE.exec(line);
    if (assignment) {
      for (const id of assignment[1].split(',')) result.classes.set(id, (result.classes.get(id) ?? new Set()).add(assignment[2]));
    }
  }
  return result;
}
