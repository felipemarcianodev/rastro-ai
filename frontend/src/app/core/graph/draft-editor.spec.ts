import { ChangeKind, EdgeConfidence, FlowGraph, NodeKind } from '../models';
import { addStep, changeCount, connect, reassign, removeEdge, removeStep, restoreStep, setIntent } from './draft-editor';

// entry -> a -> b ; entry -> c
const base = (): FlowGraph => ({
  schemaVersion: 1,
  entryPoint: 'Job.Run',
  entryNodeId: 'entry',
  nodes: [
    { id: 'entry', kind: NodeKind.Entry, label: 'Run' },
    { id: 'a', kind: NodeKind.Method, label: 'A' },
    { id: 'b', kind: NodeKind.Method, label: 'B' },
    { id: 'c', kind: NodeKind.Method, label: 'C' },
  ],
  edges: [
    { from: 'entry', to: 'a', order: 1, confidence: EdgeConfidence.Extracted },
    { from: 'a', to: 'b', order: 1, confidence: EdgeConfidence.Extracted },
    { from: 'entry', to: 'c', order: 2, confidence: EdgeConfidence.Extracted },
  ],
});

describe('draft-editor', () => {
  it('passo novo e ligação nova entram com marca de novo, no fim da sequência', () => {
    const { draft, id } = addStep(base(), ' Validar ', NodeKind.Method, 'Validar antes de enviar');
    const linked = connect(draft, 'entry', id);

    const node = linked.nodes.find((n) => n.id === id)!;
    expect(id.startsWith('new:')).toBeTrue();
    expect(node.label).toBe('Validar');
    expect(node.change).toBe(ChangeKind.Added);
    const edge = linked.edges.find((e) => e.to === id)!;
    expect(edge.change).toBe(ChangeKind.Added);
    expect(edge.order).toBe(3);
  });

  it('remover ligação: a nova some; a do código fica marcada; religar a do código desfaz a marca', () => {
    const removed = removeEdge(base(), base().edges[1]);
    expect(removed.edges[1].change).toBe(ChangeKind.Removed);

    const relinked = connect(removed, 'a', 'b');
    expect(relinked.edges.length).toBe(3);
    expect(relinked.edges[1].change).toBeUndefined();

    const withNew = connect(base(), 'c', 'b');
    expect(removeEdge(withNew, withNew.edges[3]).edges.length).toBe(3);
  });

  it('mover um passo tira a ligação antiga e cria a nova', () => {
    const graph = base();
    const moved = reassign(graph, graph.edges[1], { from: 'c' });

    expect(moved.edges.find((e) => e.from === 'a' && e.to === 'b')!.change).toBe(ChangeKind.Removed);
    expect(moved.edges.find((e) => e.from === 'c' && e.to === 'b')!.change).toBe(ChangeKind.Added);
    expect(changeCount(moved)).toBe(1);
  });

  it('conta como o prompt: passo novo com a ligação dele é uma mudança só', () => {
    const { draft, id } = addStep(base(), 'Auditar', NodeKind.Method, 'Auditar o envio');
    expect(changeCount(connect(draft, 'b', id))).toBe(1);
  });

  it('remover passo: o do código fica marcado com as ligações; restaurar desfaz; a entrada não sai', () => {
    const removed = removeStep(base(), 'a');
    expect(removed.nodes.find((n) => n.id === 'a')!.change).toBe(ChangeKind.Removed);
    expect(removed.edges.filter((e) => e.change === ChangeKind.Removed).length).toBe(2);

    const restored = restoreStep(removed, 'a');
    expect(changeCount(restored)).toBe(0);
    expect(removeStep(base(), 'entry')).toEqual(base());
  });

  it('passo novo removido some com as ligações dele', () => {
    const { draft, id } = addStep(base(), 'X', NodeKind.Decision, 'Decidir X');
    const removed = removeStep(connect(draft, 'a', id), id);
    expect(removed).toEqual(base());
  });

  it('intenção marca o passo do código como "muda"; texto vazio desfaz', () => {
    const changed = setIntent(base(), 'b', 'Enviar só em horário comercial');
    expect(changed.nodes[2].change).toBe(ChangeKind.Modified);
    expect(changeCount(changed)).toBe(1);

    expect(changeCount(setIntent(changed, 'b', '  '))).toBe(0);
  });
});

describe('draft-editor entre sistemas (Fase 25)', () => {
  const cross = (): FlowGraph => {
    const g = base();
    return {
      ...g,
      nodes: [...g.nodes, { id: 'p::x', kind: NodeKind.Method, label: 'X', system: 'pagamentos' }],
      edges: [...g.edges, { from: 'b', to: 'p::x', order: 1, confidence: EdgeConfidence.Extracted, http: { method: 'POST', route: 'api/x' } }],
    };
  };

  // UI-SYS-008
  it('edicao_passoDeOutroSistema_naoLigaNaoRemoveNemMuda', () => {
    const draft = cross();

    expect(connect(draft, 'c', 'p::x')).toBe(draft);
    expect(removeStep(draft, 'p::x')).toBe(draft);
    expect(setIntent(draft, 'p::x', 'mudar')).toBe(draft);
    expect(removeEdge(draft, draft.edges[3])).toBe(draft);
    expect(reassign(draft, draft.edges[2], { to: 'p::x' })).toBe(draft);
  });
});
