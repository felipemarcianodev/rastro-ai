import { EdgeConfidence, FlowEdge, FlowGraph, FlowNode, NodeKind } from '../models';
import { NodeEffect, effectOf, entryCodeOnly, humanName } from './reading';

const node = (id: string, kind: NodeKind, label = id): FlowNode => ({ id, kind, label });
const edge = (from: string, to: string, order: number, label?: string): FlowEdge => ({ from, to, order, label, confidence: EdgeConfidence.Extracted });

describe('leitura de relance (Fase 23)', () => {
  it('nome legível: separa as palavras, tira o Async e mantém siglas', () => {
    expect(humanName('GetPendingAlertsAsync')).toBe('Get pending alerts');
    expect(humanName('MarkSuccess')).toBe('Mark success');
    expect(humanName('SendHTTPRequest')).toBe('Send HTTP request');
    expect(humanName('Execute')).toBe('Execute');
  });

  it('o que o passo faz no mundo, pelo nome da classe e do método', () => {
    expect(effectOf(node('a', NodeKind.Method, 'OrderRepository.GetPendingOrdersAsync'))).toBe(NodeEffect.Database);
    expect(effectOf(node('b', NodeKind.Method, 'OrderNotificationService.SendAsync'))).toBe(NodeEffect.Message);
    expect(effectOf(node('c', NodeKind.External, 'IMemoryCache.TryGetValue'))).toBe(NodeEffect.Cache);
    expect(effectOf(node('d', NodeKind.Method, 'OrderEventBus.Publish'))).toBe(NodeEffect.Queue);
  });

  it('sem etiqueta: método comum, só montar o texto, e comando que olha o cache', () => {
    expect(effectOf(node('a', NodeKind.Method, 'JobScope.Begin'))).toBeUndefined();
    expect(effectOf(node('b', NodeKind.Method, 'OrderJob.FormatTelegramMessage'))).toBeUndefined();
    expect(effectOf(node('c', NodeKind.Decision, 'if (_cache.TryGetValue(key, out var v))'))).toBeUndefined();
  });

  it('modo "Resumo": só o código da entrada; o método chamado aparece fechado', () => {
    const graph: FlowGraph = {
      schemaVersion: 1,
      entryPoint: 'Job.Run',
      entryNodeId: 'job',
      nodes: [node('job', NodeKind.Entry), node('loop', NodeKind.Loop), node('send', NodeKind.Method), node('inside', NodeKind.Method), node('if', NodeKind.Decision)],
      edges: [edge('job', 'loop', 0), edge('loop', 'send', 0, 'each'), edge('send', 'inside', 0), edge('send', 'if', 1)],
    };

    const brief = entryCodeOnly(graph);

    expect(brief.nodes.map((n) => n.id)).toEqual(['job', 'loop', 'send']);
    expect(brief.edges.length).toBe(2);
  });
});
