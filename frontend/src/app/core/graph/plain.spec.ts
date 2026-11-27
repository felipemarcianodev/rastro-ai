import { FlowNode, NodeKind } from '../models';
import { plainBranch, plainLabel } from './plain';

const node = (kind: NodeKind, label: string, condition?: string): FlowNode => ({ id: 'n', kind, label, condition });

describe('texto em português na linha do tempo (Fase 20)', () => {
  it('comandos viram frase; a condição e os nomes continuam como no código', () => {
    expect(plainLabel(node(NodeKind.Decision, 'if (pendingAlerts.Count == 0)', 'pendingAlerts.Count == 0'))).toBe('Se pendingAlerts.Count == 0');
    expect(plainLabel(node(NodeKind.Decision, 'switch (status)', 'status'))).toBe('Conforme status');
    expect(plainLabel(node(NodeKind.Loop, 'foreach (var alert in pendingAlerts)', 'pendingAlerts'))).toBe('Para cada alert em pendingAlerts');
    expect(plainLabel(node(NodeKind.Loop, 'while (running)', 'running'))).toBe('Enquanto running');
    expect(plainLabel(node(NodeKind.Loop, 'for (i < n)', 'i < n'))).toBe('Repete enquanto i < n');
    expect(plainLabel(node(NodeKind.Try, 'try'))).toBe('try');
  });

  it('desfechos: erro, devolução e desvio do laço', () => {
    expect(plainLabel(node(NodeKind.Throw, 'throw new ArgumentException'))).toBe('Para com erro ArgumentException');
    expect(plainLabel(node(NodeKind.Throw, 'throw'))).toBe('Repassa o erro');
    expect(plainLabel(node(NodeKind.Return, 'return'))).toBe('Encerra');
    expect(plainLabel(node(NodeKind.Return, 'return reservation.Id'))).toBe('Encerra e devolve reservation.Id');
    expect(plainLabel(node(NodeKind.Jump, 'break'))).toBe('Sai da repetição');
    expect(plainLabel(node(NodeKind.Jump, 'continue'))).toBe('Pula para o próximo');
  });

  it('método e chamada externa ficam com o nome do código', () => {
    expect(plainLabel(node(NodeKind.Method, 'JobScope.Begin'))).toBeNull();
    expect(plainLabel(node(NodeKind.External, 'ILogger.LogInformation'))).toBeNull();
  });

  it('cabeçalho do galho', () => {
    expect(plainBranch('then')).toBe('Se sim');
    expect(plainBranch('else')).toBe('Se não');
    expect(plainBranch('catch (Exception)')).toBe('Se der erro (Exception)');
    expect(plainBranch('catch')).toBe('Se der erro');
    expect(plainBranch('finally')).toBe('Sempre ao final');
    expect(plainBranch('case Status.Pending | case Status.New')).toBe('Caso pending ou new');
    expect(plainBranch('default')).toBe('Nos outros casos');
  });

  it('case mostra só o último nome em palavras; literal fica como está (D-10)', () => {
    expect(plainBranch('case RepositoryConfigOutcome.FolderNotFound')).toBe('Caso folder not found');
    expect(plainBranch('case 1')).toBe('Caso 1');
    expect(plainBranch('case "x"')).toBe('Caso "x"');
    expect(plainBranch('case A | case B')).toBe('Caso a ou b');
    expect(plainBranch('case Status.Pending | default')).toBe('Caso pending ou outros casos');
  });
});
