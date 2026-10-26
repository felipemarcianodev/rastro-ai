import { EdgeConfidence, FlowGraph, NodeKind } from '../models';
import { LayoutDirection } from './layout';
import { SvgColors, toMermaid, toSvg } from './export';

const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'Job.Run',
  entryNodeId: 'a',
  nodes: [
    { id: 'a', kind: NodeKind.Entry, label: 'Job.Run' },
    { id: 'b', kind: NodeKind.Decision, label: 'if (x < "y")' },
    { id: 'c', kind: NodeKind.Unresolved, label: 'notifier.Notify' },
  ],
  edges: [
    { from: 'a', to: 'b', order: 0, confidence: EdgeConfidence.Extracted },
    { from: 'b', to: 'c', order: 0, confidence: EdgeConfidence.Inferred, label: 'then' },
  ],
};

const colors: SvgColors = { paper: '#fff', surface: '#fff', ink: '#000', inkSoft: '#555', line: '#ccc', edge: '#777', inferred: '#70f', confirmed: '#0a0', unresolved: '#a50' };

describe('export', () => {
  it('Mermaid na direção da tela, com forma por tipo e texto escapado', () => {
    const text = toMermaid(graph, LayoutDirection.Timeline);

    expect(text.split('\n')[0]).toBe('flowchart TD');
    expect(text).toContain('n1{"if (x #lt; #quot;y#quot;)"}');
    expect(text).toContain('n2[["notifier.Notify"]]');
    expect(text).toContain('n1 -.->|"then"| n2');
    expect(text).toContain('n0 --> n1');
  });

  it('SVG com as posições, o texto escapado e a ligação tracejada da inferida', () => {
    const boxes = new Map([
      ['a', { x: 0, y: 0, width: 200, height: 56 }],
      ['b', { x: 300, y: 0, width: 200, height: 44 }],
      ['c', { x: 600, y: 0, width: 200, height: 56 }],
    ]);
    const svg = toSvg(graph, boxes, colors);

    expect(svg.startsWith('<svg')).toBeTrue();
    expect(svg).toContain('if (x &lt; &quot;y&quot;)');
    expect(svg).toContain('stroke-dasharray="6 4"');
    expect(new DOMParser().parseFromString(svg, 'image/svg+xml').querySelector('parsererror')).toBeNull();
  });

  it('SVG leva o passo repetido e o cabeçalho do galho, como na tela (Fase 21)', () => {
    const boxes = new Map([
      ['a', { x: 0, y: 0, width: 200, height: 56 }],
      ['b', { x: 0, y: 80, width: 200, height: 44 }],
      ['c', { x: 280, y: 80, width: 200, height: 56 }],
      ['again:x', { x: 0, y: 150, width: 200, height: 56 }],
    ]);
    const svg = toSvg(graph, boxes, colors, LayoutDirection.Timeline, {
      repeats: new Map([['k', { id: 'again:x', of: 'c' }]]),
      heads: [{ x: 280, y: 60, label: 'Se não' }],
    });

    expect(svg).toContain('↻ notifier.Notify');
    expect(svg).toContain('Se não');
  });
});
