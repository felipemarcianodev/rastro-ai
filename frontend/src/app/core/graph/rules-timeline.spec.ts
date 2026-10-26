import { EdgeConfidence, FlowGraph, NodeKind, ProposalOrigin } from '../models';
import { rulesTimeline } from './rules-timeline';

const rule = (text: string) => ({ text, origin: ProposalOrigin.Agent });

// entry -> a(1) -> c ; entry -> b(2). A ordem de execução manda: entry, a, c, b.
const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'Job.Run',
  entryNodeId: 'entry',
  nodes: [
    { id: 'b', kind: NodeKind.Method, label: 'B', rule: rule('regra b') },
    { id: 'entry', kind: NodeKind.Entry, label: 'Run', rule: rule('regra entrada') },
    { id: 'c', kind: NodeKind.Decision, label: 'if', rule: rule('regra c') },
    { id: 'a', kind: NodeKind.Method, label: 'A' },
  ],
  edges: [
    { from: 'entry', to: 'b', order: 2, confidence: EdgeConfidence.Extracted },
    { from: 'entry', to: 'a', order: 1, confidence: EdgeConfidence.Extracted },
    { from: 'a', to: 'c', order: 1, confidence: EdgeConfidence.Extracted },
  ],
};

describe('rulesTimeline', () => {
  it('lista só os nós com regra, na ordem em que o fluxo passa por eles', () => {
    expect(rulesTimeline(graph).map((n) => n.id)).toEqual(['entry', 'c', 'b']);
  });
});
