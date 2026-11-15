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
  // As seções do try ficam com o nome do código, como o próprio try.
  if (label === BranchLabel.Finally || label === BranchLabel.Catch || label.startsWith(`${BranchLabel.Catch} `)) return label;
  if (label === BranchLabel.Default) return tr('graph.plain.branchDefault');
  return label
    .split(' | ')
    .map((part) => (part === BranchLabel.Default ? tr('graph.plain.branchOtherCases') : part.startsWith('case ') ? part.slice('case '.length) : part))
    .reduce((text, part, index) => (index === 0 ? tr('graph.plain.branchCase', { value: part }) : tr('graph.plain.branchOr', { text, part })), '');
}

function inner(label: string): string {
  return PAREN.exec(label)?.[1] ?? label;
}
