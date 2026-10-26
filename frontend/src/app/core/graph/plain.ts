import { FlowNode, NodeKind } from '../models';
import { BranchLabel } from './branches';

// Linha do tempo (Fase 20): os comandos em português, para um júnior ou uma pessoa que não
// programa entender o fluxo. A condição e os nomes continuam como estão no código; só a palavra
// do comando muda. O texto original fica no title do passo.

const FOREACH = /^foreach \((?:.+\s)?(\S+) in (.+)\)$/;
const PAREN = /^\w+ \((.*)\)$/;

const JUMP_TEXT: Record<string, string> = {
  break: 'Sai da repetição',
  continue: 'Pula para o próximo',
};

// Texto do passo em português, ou null quando o próprio rótulo já serve (método, chamada externa).
export function plainLabel(node: FlowNode): string | null {
  const label = node.label;
  switch (node.kind) {
    case NodeKind.Decision:
      if (label.startsWith('if (')) return `Se ${node.condition ?? inner(label)}`;
      if (label.startsWith('switch (')) return `Conforme ${node.condition ?? inner(label)}`;
      return null;
    case NodeKind.Loop: {
      const each = FOREACH.exec(label);
      if (each) return `Para cada ${each[1]} em ${each[2]}`;
      const condition = node.condition ?? inner(label);
      if (label.startsWith('while (')) return `Enquanto ${condition}`;
      return condition ? `Repete enquanto ${condition}` : 'Repete';
    }
    // O bloco fica com o nome do código: "Tenta" deixava vago (pedido do dono).
    case NodeKind.Try:
      return 'try';
    case NodeKind.Throw:
      if (label === 'throw') return 'Repassa o erro';
      if (label.startsWith('throw new ')) return `Para com erro ${label.slice('throw new '.length)}`;
      return `Para com erro: ${label.slice('throw '.length)}`;
    case NodeKind.Return:
      return label === 'return' ? 'Encerra' : `Encerra e devolve ${label.slice('return '.length)}`;
    case NodeKind.Jump:
      return JUMP_TEXT[label] ?? null;
    default:
      return null;
  }
}

// Cabeçalho do galho e da seção do bloco em português. "then" só ganha texto quando há "else".
export function plainBranch(label: string): string {
  if (label === BranchLabel.Then) return 'Se sim';
  if (label === BranchLabel.Else) return 'Se não';
  // As seções do try ficam com o nome do código, como o próprio try.
  if (label === BranchLabel.Finally || label === BranchLabel.Catch || label.startsWith(`${BranchLabel.Catch} `)) return label;
  if (label === BranchLabel.Default) return 'Nos outros casos';
  return label
    .split(' | ')
    .map((part) => (part === BranchLabel.Default ? 'outros casos' : part.startsWith('case ') ? part.slice('case '.length) : part))
    .reduce((text, part, index) => (index === 0 ? `Caso ${part}` : `${text} ou ${part}`), '');
}

function inner(label: string): string {
  return PAREN.exec(label)?.[1] ?? label;
}
