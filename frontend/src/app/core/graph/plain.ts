import { tr } from '../i18n/locale';
import { FlowNode, NodeKind } from '../models';
import { BranchLabel } from './branches';

// Linha do tempo (Fase 20): os comandos em linguagem de gente, para um júnior ou uma pessoa que
// não programa entender o fluxo. A condição e os nomes continuam como estão no código; só a
// palavra do comando muda (e acompanha o idioma da tela). O texto original fica no title do passo.

const FOREACH = /^foreach \((?:.+\s)?(\S+) in (.+)\)$/;
const PAREN = /^\w+ \((.*)\)$/;

const JUMP_TEXT_KEY: Record<string, string> = {
  break: 'graph.plain.jump.break',
  continue: 'graph.plain.jump.continue',
};

// Texto do passo no idioma da tela, ou null quando o próprio rótulo já serve (método, chamada externa).
export function plainLabel(node: FlowNode): string | null {
  const label = node.label;
  switch (node.kind) {
    case NodeKind.Decision:
      if (label.startsWith('if (')) return tr('graph.plain.if', { condition: node.condition ?? inner(label) });
      if (label.startsWith('switch (')) return tr('graph.plain.switch', { condition: node.condition ?? inner(label) });
      return null;
    case NodeKind.Loop: {
      const each = FOREACH.exec(label);
      if (each) return tr('graph.plain.foreach', { item: each[1], collection: each[2] });
      const condition = node.condition ?? inner(label);
      if (label.startsWith('while (')) return tr('graph.plain.while', { condition });
      return condition ? tr('graph.plain.repeatWhile', { condition }) : tr('graph.plain.repeat');
    }
    // O bloco fica com o nome do código: "Tenta" deixava vago (pedido do dono).
    case NodeKind.Try:
      return 'try';
    case NodeKind.Throw:
      if (label === 'throw') return tr('graph.plain.rethrow');
      if (label.startsWith('throw new ')) return tr('graph.plain.throwNew', { error: label.slice('throw new '.length) });
      return tr('graph.plain.throw', { error: label.slice('throw '.length) });
    case NodeKind.Return:
      return label === 'return' ? tr('graph.plain.end') : tr('graph.plain.endReturning', { value: label.slice('return '.length) });
    case NodeKind.Jump:
      return JUMP_TEXT_KEY[label] ? tr(JUMP_TEXT_KEY[label]) : null;
    default:
      return null;
  }
}

// Cabeçalho do galho e da seção do bloco no idioma da tela. "then" só ganha texto quando há "else".
export function plainBranch(label: string): string {
  if (label === BranchLabel.Then) return tr('graph.plain.branchThen');
  if (label === BranchLabel.Else) return tr('graph.plain.branchElse');
  // As seções do try em linguagem de leitura (D-02); o rótulo bruto fica no código e no title.
  if (label === BranchLabel.Finally) return tr('graph.plain.branchFinally');
  if (label === BranchLabel.Catch || label.startsWith(`${BranchLabel.Catch} `)) {
    const type = CATCH_TYPE.exec(label)?.[1]?.trim();
    return type ? tr('graph.plain.branchCatchType', { type }) : tr('graph.plain.branchCatch');
  }
  if (label === BranchLabel.Default) return tr('graph.plain.branchDefault');
  return label
    .split(' | ')
    .map((part) => (part === BranchLabel.Default ? tr('graph.plain.branchOtherCases') : part.startsWith('case ') ? caseValue(part.slice('case '.length)) : part))
    .reduce((text, part, index) => (index === 0 ? tr('graph.plain.branchCase', { value: part }) : tr('graph.plain.branchOr', { text, part })), '');
}

const CATCH_TYPE = /^catch\s*\((.*)\)$/;
const IDENTIFIER_PATH = /^@?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*$/;
const WORD_BOUNDARY = /(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|[_-]+/;
const LITERAL_WORDS = new Set(['true', 'false', 'null', 'undefined']);

// Valor do case em palavras (D-10): "RepositoryConfigOutcome.FolderNotFound" vira "folder not found".
// Literal (1, "x", true) e padrão (> 5, Foo f when ...) ficam como estão.
function caseValue(value: string): string {
  const text = value.trim();
  if (!IDENTIFIER_PATH.test(text) || LITERAL_WORDS.has(text)) return text;
  const last = text.slice(text.lastIndexOf('.') + 1).replace(/^@/, '');
  return last
    .split(WORD_BOUNDARY)
    .filter((word) => word.length > 0)
    .map((word) => (word.length > 1 && word === word.toUpperCase() ? word : word.toLowerCase()))
    .join(' ');
}

function inner(label: string): string {
  return PAREN.exec(label)?.[1] ?? label;
}
