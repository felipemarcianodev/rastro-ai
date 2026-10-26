import { EdgeConfidence, FlowGraph, NodeKind } from '../models';
import { isFrameworkCall, withoutFrameworkCalls } from './visibility';

const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'Job.Run',
  entryNodeId: 'entry',
  nodes: [
    { id: 'entry', kind: NodeKind.Entry, label: 'Job.Run' },
    { id: 'ef', kind: NodeKind.External, label: 'IQueryable.ToListAsync', symbol: 'Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync<T>()' },
    { id: 'http', kind: NodeKind.External, label: 'HttpClient.PostAsync', symbol: 'System.Net.Http.HttpClient.PostAsync()' },
    { id: 'stripe', kind: NodeKind.External, label: 'CustomerService.CreateAsync', symbol: 'Stripe.CustomerService.CreateAsync()' },
  ],
  edges: [
    { from: 'entry', to: 'ef', order: 1, confidence: EdgeConfidence.Extracted },
    { from: 'entry', to: 'http', order: 2, confidence: EdgeConfidence.Extracted },
    { from: 'entry', to: 'stripe', order: 3, confidence: EdgeConfidence.Extracted },
  ],
};

describe('visibility', () => {
  it('chamada do .NET é System.* ou Microsoft.*; SDK de terceiro não é', () => {
    expect(isFrameworkCall(graph.nodes[1])).toBeTrue();
    expect(isFrameworkCall(graph.nodes[2])).toBeTrue();
    expect(isFrameworkCall(graph.nodes[3])).toBeFalse();
    expect(isFrameworkCall(graph.nodes[0])).toBeFalse();
  });

  it('esconder tira os nós do .NET e as arestas até eles, e mantém o resto', () => {
    const filtered = withoutFrameworkCalls(graph);

    expect(filtered.nodes.map((n) => n.id)).toEqual(['entry', 'stripe']);
    expect(filtered.edges.map((e) => e.to)).toEqual(['stripe']);
  });
});
