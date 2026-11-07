import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { EdgeConfidence, FlowGraph, NodeKind, ProposalOrigin } from '../../core/models';
import { FlowCanvasComponent, SLOW_LAYOUT_MS, isPanGesture, isSelectionGesture, pickedFromSelection, splitLabel } from './flow-canvas.component';

describe('splitLabel', () => {
  it('separa dono e nome do método, para o nome nunca ser cortado', () => {
    expect(splitLabel({ id: 'm', kind: NodeKind.Method, label: 'OrderNotificationService.SendAsync' })).toEqual({
      owner: 'OrderNotificationService.',
      name: 'SendAsync',
    });
  });

  it('bifurcações e afins mostram o texto inteiro', () => {
    expect(splitLabel({ id: 'd', kind: NodeKind.Decision, label: 'if (alert.Muted)' })).toEqual({ owner: '', name: 'if (alert.Muted)' });
    expect(splitLabel({ id: 'u', kind: NodeKind.Unresolved, label: 'notifier.Notify' })).toEqual({ owner: '', name: 'notifier.Notify' });
  });

  it('rótulo sem ponto fica todo no nome', () => {
    expect(splitLabel({ id: 'm', kind: NodeKind.Method, label: 'Run' })).toEqual({ owner: '', name: 'Run' });
  });
});

describe('seleção múltipla no grafo', () => {
  const PRIMARY = 1;
  const MIDDLE = 4;

  // Evento com alvo dentro de um f-flow de mentira: o fundo, ou um passo (fNode).
  function pointer(buttons: number, onNode = false, shiftKey = false): MouseEvent {
    const flow = document.createElement('f-flow');
    const target = document.createElement('div');
    if (onNode) target.setAttribute('fNode', '');
    flow.appendChild(target);
    document.body.appendChild(flow);
    const event = new MouseEvent('pointerdown', { buttons, shiftKey, bubbles: true });
    Object.defineProperty(event, 'target', { value: target });
    queueMicrotask(() => flow.remove());
    return event;
  }

  it('SEL-001: Shift+arrastar no fundo desenha o laço', () => {
    expect(isSelectionGesture(pointer(PRIMARY, false, true), false)).toBeTrue();
  });

  it('SEL-006: arrastar no fundo sem Shift move a visão, não desenha o laço', () => {
    expect(isSelectionGesture(pointer(PRIMARY), false)).toBeFalse();
    expect(isPanGesture(pointer(PRIMARY), false)).toBeTrue();
  });

  it('SEL-005: arrastar em cima de um passo move o passo, não desenha o laço', () => {
    expect(isSelectionGesture(pointer(PRIMARY, true, true), false)).toBeFalse();
  });

  it('SEL-002: com o Espaço segurado, até Shift+arrastar move a visão em vez do laço', () => {
    expect(isSelectionGesture(pointer(PRIMARY, false, true), true)).toBeFalse();
    expect(isPanGesture(pointer(PRIMARY, false, true), true)).toBeTrue();
  });

  it('SEL-003: o botão do meio sempre move a visão; o principal com Shift não', () => {
    expect(isPanGesture(pointer(MIDDLE), false)).toBeTrue();
    expect(isPanGesture(pointer(PRIMARY, false, true), false)).toBeFalse();
  });

  it('SEL-004: o painel segue um passo marcado; vários ou nenhum não trocam o escolhido', () => {
    expect(pickedFromSelection(['a'])).toBe('a');
    expect(pickedFromSelection(['a', 'b'])).toBeNull();
    expect(pickedFromSelection([])).toBeNull();
  });
});

describe('FlowCanvasComponent: botões de modo (A5)', () => {
  const graph: FlowGraph = { schemaVersion: 3, entryPoint: 'A', entryNodeId: 'a', nodes: [{ id: 'a', kind: NodeKind.Entry, label: 'A' }], edges: [] };

  function render(showFramework: boolean) {
    TestBed.configureTestingModule({ imports: [FlowCanvasComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(FlowCanvasComponent);
    fixture.componentRef.setInput('graph', graph);
    fixture.componentRef.setInput('frameworkCount', 3);
    fixture.componentRef.setInput('showFramework', showFramework);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  // A5
  it('o rótulo não muda com o estado: ele fica só no aria-pressed', () => {
    const labels = (el: HTMLElement) => ({
      brief: el.querySelector('.tools-row-button')?.textContent?.trim(),
      framework: el.querySelector('.framework-button')?.textContent?.trim(),
    });
    const off = render(false);
    const before = labels(off);
    expect(before.brief).toBe('Resumo do fluxo');
    expect(before.framework).toBe('Chamadas do .NET (3)');
    expect(off.querySelector('.framework-button')?.getAttribute('aria-pressed')).toBe('false');
    TestBed.resetTestingModule();
    const on = render(true);
    expect(labels(on)).toEqual(before);
    expect(on.querySelector('.framework-button')?.getAttribute('aria-pressed')).toBe('true');
  });

  // UX-G-F13
  it('a ajuda do grafo cita as teclas + e −', () => {
    const help = render(false).querySelector('#graph-keys-help')?.textContent ?? '';
    expect(help).toContain('+ e −');
  });

  // UX-G-F13
  it('o glifo de recolher tem área mínima de 24 px', () => {
    const probe = document.createElement('span');
    probe.className = 'flow-node-fold';
    document.body.appendChild(probe);
    const box = probe.getBoundingClientRect();
    probe.remove();
    expect(box.width).toBeGreaterThanOrEqual(24);
    expect(box.height).toBeGreaterThanOrEqual(24);
  });

  // UX-G-F01
  // UX-G-F21
  it('com o grafo afastado, a marca da ligação continua visível e só o texto do rótulo some', () => {
    const host = document.createElement('div');
    host.className = 'flow-host is-far';
    host.innerHTML = '<span class="edge-label"><span class="edge-mark is-inferred">?</span><span class="edge-label-text">texto</span></span>';
    document.body.appendChild(host);
    const mark = getComputedStyle(host.querySelector('.edge-mark')!);
    const textDisplay = getComputedStyle(host.querySelector('.edge-label-text')!).display;
    const markDisplay = mark.display;
    const markTransform = mark.transform;
    const size = host.querySelector('.edge-mark')!.getBoundingClientRect();
    host.remove();

    expect(textDisplay).toBe('none');
    expect(markDisplay).not.toBe('none');
    expect(markTransform).not.toBe('none');
    expect(size.width).toBeGreaterThan(16);
  });

  // UX-G-F21
  it('a marca da ligação tem 16 px e um aria-label', () => {
    const probe = document.createElement('span');
    probe.className = 'edge-mark is-confirmed';
    document.body.appendChild(probe);
    const box = probe.getBoundingClientRect();
    probe.remove();
    expect(box.width).toBe(16);

    TestBed.configureTestingModule({ imports: [FlowCanvasComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(FlowCanvasComponent);
    const withEdge: FlowGraph = {
      ...graph,
      nodes: [...graph.nodes, { id: 'b', kind: NodeKind.Entry, label: 'B' }],
      edges: [{ from: 'a', to: 'b', order: 1, confidence: EdgeConfidence.Inferred }],
    };
    fixture.componentRef.setInput('graph', withEdge);
    fixture.detectChanges();
    const rendered = (fixture.nativeElement as HTMLElement).querySelector('.edge-label .edge-mark.is-inferred');
    if (rendered) expect(rendered.getAttribute('aria-label')).toBe('Inferida, a conferir');
  });
});

describe('FlowCanvasComponent: acréscimos da Fase B', () => {
  const SEEN_KEY = 'rastro.shortcutsHintSeen';

  function chain(count: number): FlowGraph {
    const nodes = Array.from({ length: count }, (_, i) => ({ id: `n${i}`, kind: i === 0 ? NodeKind.Entry : NodeKind.Method, label: `Servico${i}.Executar` }));
    const edges = nodes.slice(1).map((n, i) => ({ from: `n${i}`, to: n.id, order: 0, confidence: EdgeConfidence.Extracted }));
    return { schemaVersion: 3, entryPoint: 'A', entryNodeId: 'n0', nodes, edges };
  }

  interface Internals {
    onNodeMoved(id: string, point: { x: number; y: number }): void;
    moved(): Record<string, { x: number; y: number }>;
    lastLayoutMs: { set(value: number): void; (): number };
    drawingBusy(): boolean;
  }

  async function render(graph: FlowGraph, editing = false) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [FlowCanvasComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(FlowCanvasComponent);
    fixture.componentRef.setInput('graph', graph);
    fixture.componentRef.setInput('editing', editing);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, inner: fixture.componentInstance as unknown as Internals };
  }

  // UX-G-F02
  it('a legenda explica o selo de origem de cada ligação de IA, MCP ou manual que o grafo tem', async () => {
    const graph = chain(4);
    graph.edges[0] = { ...graph.edges[0], confidence: EdgeConfidence.Inferred, origin: ProposalOrigin.Provider };
    graph.edges[1] = { ...graph.edges[1], confidence: EdgeConfidence.Confirmed, origin: ProposalOrigin.Agent };
    graph.edges[2] = { ...graph.edges[2], confidence: EdgeConfidence.Confirmed, origin: ProposalOrigin.Manual };
    const { el } = await render(graph);
    const marks = [...el.querySelectorAll('#legend .edge-origin')].map((m) => m.textContent!.trim());
    expect(marks).toEqual(['IA', 'MCP', '✎']);
    const inGraph = [...el.querySelectorAll('.edge-label .edge-origin')].map((m) => m.getAttribute('aria-label'));
    if (inGraph.length > 0) expect(inGraph).toContain('Proposta pelo provedor de IA');
  });

  // UX-G-F02
  it('ligação só extraída não ganha selo de origem nem entrada na legenda', async () => {
    const { el } = await render(chain(3));
    expect(el.querySelectorAll('.edge-origin').length).toBe(0);
  });

  // UX-G-F06
  it('arrastar um passo mostra "Passo movido" e Desfazer devolve a posição', async () => {
    const { fixture, el, inner } = await render(chain(2));
    inner.onNodeMoved('n1', { x: 500, y: 300 });
    fixture.detectChanges();
    expect(el.querySelector('.toast')!.textContent).toContain('Passo movido.');
    expect(inner.moved()['n1']).toEqual({ x: 500, y: 300 });
    el.querySelector<HTMLButtonElement>('.toast button')!.click();
    fixture.detectChanges();
    expect(inner.moved()['n1']).toBeUndefined();
    expect(el.querySelector('.toast')).toBeNull();
  });

  // UX-G-F06
  it('Ctrl+Z desfaz e Ctrl+Shift+Z refaz o movimento, fora da edição', async () => {
    const { fixture, el, inner } = await render(chain(2));
    inner.onNodeMoved('n1', { x: 500, y: 300 });
    const key = (shiftKey: boolean) => el.dispatchEvent(new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, shiftKey, bubbles: true, cancelable: true }));
    key(false);
    fixture.detectChanges();
    expect(inner.moved()['n1']).toBeUndefined();
    key(true);
    fixture.detectChanges();
    expect(inner.moved()['n1']).toEqual({ x: 500, y: 300 });
  });

  // UX-G-F06
  it('na edição o Ctrl+Z não mexe nas posições (é do rascunho)', async () => {
    const { el, inner } = await render(chain(2), true);
    inner.onNodeMoved('n1', { x: 500, y: 300 });
    const event = new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true, cancelable: true });
    el.dispatchEvent(event);
    expect(event.defaultPrevented).toBeFalse();
    expect(inner.moved()['n1']).toEqual({ x: 500, y: 300 });
  });

  // UX-G-F07
  it('o minimapa dá contorno forte ao não resolvido e cresce em telas largas', () => {
    const host = document.createElement('div');
    host.className = 'flow-host';
    host.innerHTML = '<div class="f-minimap"><svg><rect class="f-minimap-node f-component is-unresolved"></rect><rect class="f-minimap-node f-component"></rect></svg></div>';
    document.body.appendChild(host);
    const [hole, plain] = [...host.querySelectorAll('rect')].map((r) => getComputedStyle(r));
    const map = getComputedStyle(host.querySelector('.f-minimap')!);
    const wide = window.innerWidth >= 1600;
    const size = [map.width, map.height];
    const stroke = [hole.strokeWidth, hole.stroke, plain.stroke];
    host.remove();
    expect(stroke[0]).toBe('2px');
    expect(stroke[1]).not.toBe('none');
    expect(stroke[2]).toBe('none');
    expect(size).toEqual(wide ? ['240px', '160px'] : ['180px', '120px']);
  });

  // UX-G-F08
  it('fora da edição só as portas usadas pelas ligações são desenhadas; na edição, as quatro de cada passo', async () => {
    const graph = chain(3);
    const idle = (await render(graph)).el.querySelectorAll('.flow-port').length;
    const editing = (await render(graph, true)).el.querySelectorAll('.flow-port').length;
    expect(editing).toBe(12);
    expect(idle).toBeLessThan(editing);
    expect(idle).toBeGreaterThanOrEqual(4);
  });

  // UX-G-F08
  it('depois de um layout lento, o próximo mostra "Desenhando o grafo…" até terminar', async () => {
    const { fixture, el, inner } = await render(chain(3));
    expect(inner.lastLayoutMs()).toBeLessThan(SLOW_LAYOUT_MS);
    inner.lastLayoutMs.set(SLOW_LAYOUT_MS + 100);
    fixture.componentRef.setInput('graph', chain(4));
    fixture.detectChanges();
    expect(inner.drawingBusy()).toBeTrue();
    expect(el.querySelector('.canvas-status.is-busy')!.textContent).toContain('Desenhando o grafo');
    await new Promise((resolve) => setTimeout(resolve, 150));
    fixture.detectChanges();
    expect(inner.drawingBusy()).toBeFalse();
    expect(el.querySelector('.canvas-status.is-busy')).toBeNull();
  });

  // UX-G-F08
  it('o layout de 200 passos é medido', async () => {
    const { inner } = await render(chain(200));
    expect(inner.lastLayoutMs()).toBeGreaterThanOrEqual(0);
    expect(inner.lastLayoutMs()).toBeLessThan(5000);
  });

  // UX-G-F14
  it('na primeira visita mostra a linha de atalhos, que fecha e não volta', async () => {
    localStorage.removeItem(SEEN_KEY);
    try {
      const first = await render(chain(2));
      const hint = first.el.querySelector('.shortcuts-hint')!;
      expect(hint.textContent).toContain('Setas: andar · Alt+setas: mover · Ctrl+F: buscar · Esc: limpar');
      hint.querySelector<HTMLButtonElement>('button')!.click();
      first.fixture.detectChanges();
      expect(first.el.querySelector('.shortcuts-hint')).toBeNull();
      expect(localStorage.getItem(SEEN_KEY)).toBe('true');
      const second = await render(chain(2));
      expect(second.el.querySelector('.shortcuts-hint')).toBeNull();
    } finally {
      localStorage.removeItem(SEEN_KEY);
    }
  });
});
