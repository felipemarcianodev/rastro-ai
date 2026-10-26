import { NodeKind } from '../models';
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
