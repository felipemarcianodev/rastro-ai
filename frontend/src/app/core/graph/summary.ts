import { FlowGraph, FlowNode, NodeKind } from '../models';
import { NODE_EFFECT_LABEL, NodeEffect, humanName, splitLabel } from './reading';
import { plainLabel } from './plain';
import { OutlineItem } from './timeline-layout';

// Resumo do fluxo (Fase 23): uma frase com a linha principal e a lista numerada dos passos, com
// a mesma numeração do grafo. Sai do desenho, sem IA: o mesmo grafo dá o mesmo resumo.

// O resumo como a visão geral mostra: a frase e a lista numerada, já com o texto de cada passo.
export interface SummaryItem {
  // Passo a escolher no grafo (o original, quando o item é um passo repetido).
  id: string;
  number: string;
  text: string;
  // Classe do método, quando o texto é o nome legível dele.
  detail?: string;
  // "banco", "mensagem", "cache" (o que o passo faz no mundo).
  effect?: string;
  head?: string;
  repeat: boolean;
  children: SummaryItem[];
}

export interface FlowSummary {
  sentence: string;
  items: SummaryItem[];
}

export interface StepText {
  text: string;
  kind: NodeKind;
}

// Comandos cujo conteúdo entra na frase (o que se tenta, o que se repete, o que se decide).
// O que um método chama fica de fora: é detalhe, e a lista mostra.
const OPENED_IN_SENTENCE = new Set([NodeKind.Try, NodeKind.Loop, NodeKind.Decision]);

export function summarySentence(outline: readonly OutlineItem[], textOf: (item: OutlineItem) => StepText): string {
  const phrase = (item: OutlineItem): string => {
    const { text, kind } = textOf(item);
    if (!OPENED_IN_SENTENCE.has(kind) || item.children.length === 0) return text;
    // Os filhos em trechos: cada cabeçalho ("catch (Exception)", "Se não") abre um trecho novo.
    const parts: { head?: string; texts: string[] }[] = [];
    for (const child of item.children) {
      if (child.head || parts.length === 0) parts.push({ head: child.head, texts: [] });
      parts[parts.length - 1].texts.push(lowerFirst(textOf(child).text));
    }
    const inner = parts.map((part, index) => `${part.head ? `${index === 0 ? part.head : lowerFirst(part.head)}: ` : ''}${part.texts.join(', ')}`).join('; ');
    return `${text}: ${inner}`;
  };
  const sentence = outline.map((item, index) => (index === 0 ? phrase(item) : lowerFirst(phrase(item)))).join('; ');
  return sentence ? `${sentence}.` : '';
}

function lowerFirst(text: string): string {
  // Sigla ou nome de código ("HTTP", "IMemoryCache") fica como está.
  return /^[A-Z][a-z]/.test(text) ? text.charAt(0).toLowerCase() + text.slice(1) : text;
}

// Texto de leitura do passo (Fases 20 a 23): o comando em português; o método pelo nome legível.
export function readingLabel(node: FlowNode): string {
  return plainLabel(node) ?? (node.kind === NodeKind.Method || node.kind === NodeKind.Entry ? humanName(splitLabel(node).name) : node.label);
}

// Resumo do fluxo para a visão geral do painel (Fase 23), a partir do resumo em árvore do desenho.
// "drawn" é o grafo desenhado (o texto dele); "originals", os nós do levantamento (Fase 28: o
// canvas e o desenho consolidado montam o mesmo resumo).
export function flowSummary(
  outline: readonly OutlineItem[],
  drawn: FlowGraph,
  originals: ReadonlyMap<string, FlowNode>,
  effects: ReadonlyMap<string, NodeEffect>,
): FlowSummary {
  const shown = new Map(drawn.nodes.map((n) => [n.id, n]));
  const textOf = (item: OutlineItem) => {
    const node = originals.get(item.of);
    return { text: shown.get(item.of)?.label ?? node?.label ?? item.of, kind: node?.kind ?? NodeKind.Method };
  };
  const toItem = (item: OutlineItem): SummaryItem => {
    const node = originals.get(item.of);
    const method = node && (node.kind === NodeKind.Method || node.kind === NodeKind.Entry);
    const effect = effects.get(item.of);
    return {
      id: item.of,
      number: item.number,
      text: textOf(item).text,
      ...(method ? { detail: splitLabel(node).owner.replace(/\.$/, '') } : {}),
      ...(effect !== undefined ? { effect: NODE_EFFECT_LABEL[effect] } : {}),
      ...(item.head ? { head: item.head } : {}),
      repeat: item.id !== item.of,
      children: item.children.map(toItem),
    };
  };
  return { sentence: summarySentence(outline, textOf), items: outline.map(toItem) };
}
