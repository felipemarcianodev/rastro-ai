import { EdgeConfidence, FlowGraph, NodeKind } from '../models';
import { searchNodes } from './search';

const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'Job.Run',
  entryNodeId: 'a',
  nodes: [
    { id: 'a', kind: NodeKind.Entry, label: 'Job.Run' },
    { id: 'b', kind: NodeKind.Method, label: 'NotificaçãoService.Enviar', symbol: 'App.NotificaçãoService.Enviar(string)' },
    { id: 'c', kind: NodeKind.Method, label: 'TelegramClient.SendAsync' },
    { id: 'd', kind: NodeKind.Decision, label: 'if (alerta.Silenciado)' },
  ],
  edges: [{ from: 'a', to: 'b', order: 0, confidence: EdgeConfidence.Extracted }],
};

describe('searchNodes', () => {
  it('acha sem acento e sem caixa, na ordem do grafo', () => {
    expect(searchNodes(graph, 'notificacao')).toEqual(['b']);
    expect(searchNodes(graph, 'SEND')).toEqual(['c']);
  });

  it('exige todos os termos e procura também no símbolo', () => {
    expect(searchNodes(graph, 'app enviar')).toEqual(['b']);
    expect(searchNodes(graph, 'app telegram')).toEqual([]);
  });

  it('consulta vazia não acha nada', () => {
    expect(searchNodes(graph, '   ')).toEqual([]);
  });
});
