import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { FlowGraph, NodeKind } from '../../core/models';
import { FlowCanvasComponent, isPanGesture, isSelectionGesture, pickedFromSelection, splitLabel } from './flow-canvas.component';

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
});
