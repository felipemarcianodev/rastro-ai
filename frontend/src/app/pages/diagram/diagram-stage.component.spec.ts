import { Component, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DiagramStageComponent, StagePick } from './diagram-stage.component';

// SVG com o formato de id que o Mermaid dá aos passos (flowchart-<id>-<n>).
const SVG = `<svg viewBox="0 0 400 800">
  <g class="node" id="flowchart-sa-0" transform="translate(50,50)"><rect width="80" height="30"/><text>Passo A</text></g>
  <g class="node" id="flowchart-sb-1" transform="translate(50,700)"><rect width="80" height="30"/><text>Passo B</text></g>
  <g class="node" id="flowchart-sb_r1-2" transform="translate(250,700)"><rect width="80" height="30"/><text>↻ Passo B</text></g>
</svg>`;

@Component({
  imports: [DiagramStageComponent],
  template: `<app-diagram-stage style="display:block;width:400px;height:300px" [svg]="svg()" [selected]="selected()" [repeats]="repeats()" (pick)="picks.push($event)" />`,
})
class HostComponent {
  readonly svg = signal(SVG);
  readonly selected = signal<string | null>(null);
  readonly repeats = signal(false);
  readonly picks: (StagePick | null)[] = [];
  readonly stage = viewChild.required(DiagramStageComponent);
}

function setup() {
  TestBed.configureTestingModule({ imports: [HostComponent] });
  const fixture = TestBed.createComponent(HostComponent);
  document.body.appendChild(fixture.nativeElement);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const viewport = el.querySelector('.stage-viewport') as HTMLElement;
  const content = () => el.querySelector('.stage-content') as HTMLElement;
  return { fixture, el, viewport, content, host: fixture.componentInstance };
}

// translate(Xpx, Ypx) scale(S) -> [X, Y, S]
function view(content: HTMLElement): number[] {
  // Número com expoente também ("1e-14px", quando a conta dá quase zero).
  const num = String.raw`(-?[\d.]+(?:e[-+]?\d+)?)`;
  const match = new RegExp(String.raw`translate\(${num}px, ${num}px\) scale\(${num}\)`).exec(content.style.transform);
  return match ? match.slice(1).map(Number) : [];
}

function pointer(target: Element, type: string, x: number, y: number): void {
  target.dispatchEvent(new PointerEvent(type, { bubbles: true, button: 0, pointerId: 7, clientX: x, clientY: y }));
}

describe('DiagramStageComponent (Fase 28)', () => {
  afterEach(() => document.querySelectorAll('app-diagram-stage').forEach((n) => n.parentElement?.remove()));

  // WEB-STG-001
  it('fluxoAlto_abreNaLarguraDoTopoEEnquadrarMostraTudo', () => {
    const { fixture, el, content } = setup();
    const [x, y, scale] = view(content());

    // Inteiro, 800 de altura numa vista de 300 daria 31%: ilegível. Abre na largura (400 - 2 * 24).
    expect(scale).toBeCloseTo((400 - 48) / 400, 3);
    expect(x).toBeCloseTo(24, 1);
    expect(y).toBe(24);
    expect(content().querySelector('svg')!.getAttribute('width')).toBe('400');

    (el.querySelector('[aria-label^="Enquadrar"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(view(content())[2]).toBeCloseTo((300 - 48) / 800, 3);
  });

  // WEB-STG-002
  it('rodaDoMouse_aproximaEmVoltaDoCursor', () => {
    const { fixture, viewport, content } = setup();
    const [x0, y0, s0] = view(content());
    const rect = viewport.getBoundingClientRect();
    const px = 100;
    const py = 80;
    // Ponto do desenho sob o cursor antes do zoom.
    const before = [(px - x0) / s0, (py - y0) / s0];

    viewport.dispatchEvent(new WheelEvent('wheel', { deltaY: -200, clientX: rect.left + px, clientY: rect.top + py, bubbles: true, cancelable: true }));
    fixture.detectChanges();
    const [x1, y1, s1] = view(content());

    expect(s1).toBeGreaterThan(s0);
    expect((px - x1) / s1).toBeCloseTo(before[0], 3);
    expect((py - y1) / s1).toBeCloseTo(before[1], 3);
  });

  // WEB-STG-003
  it('arrastarOFundo_moveODesenhoESemEscolherPasso', () => {
    const { fixture, viewport, content, host } = setup();
    const [x0, y0] = view(content());

    pointer(viewport, 'pointerdown', 100, 100);
    pointer(viewport, 'pointermove', 140, 70);
    pointer(viewport, 'pointerup', 140, 70);
    fixture.detectChanges();
    const [x1, y1] = view(content());

    expect(x1 - x0).toBeCloseTo(40, 3);
    expect(y1 - y0).toBeCloseTo(-30, 3);
    expect(host.picks.length).toBe(0);
  });

  // WEB-STG-004
  it('clicarNoPasso_escolheEClicarNoFundo_limpa', () => {
    const { viewport, el, host } = setup();
    const node = el.querySelector('#flowchart-sb-1')!;

    pointer(node, 'pointerdown', 10, 10);
    pointer(node, 'pointerup', 11, 10);
    pointer(viewport, 'pointerdown', 5, 5);
    pointer(viewport, 'pointerup', 5, 5);

    expect(host.picks[0]?.id).toBe('sb');
    expect(host.picks[0]?.label).toBe('Passo B');
    expect(host.picks[1]).toBeNull();
  });

  // WEB-STG-005
  it('botoes_aproximamAfastamEVoltamA100', () => {
    const { fixture, el, content } = setup();
    const click = (label: string) => {
      (el.querySelector(`[aria-label^="${label}"]`) as HTMLButtonElement).click();
      fixture.detectChanges();
    };
    const [, , s0] = view(content());

    click('Aproximar');
    expect(view(content())[2]).toBeCloseTo(s0 * 1.2, 3);
    click('Afastar');
    expect(view(content())[2]).toBeCloseTo(s0, 3);
    click('Zoom em');
    expect(view(content())[2]).toBe(1);
    expect(el.textContent).toContain('100%');
    click('Enquadrar');
    expect(view(content())[2]).toBeCloseTo((300 - 48) / 800, 3);
  });

  // WEB-STG-006
  it('selecaoComRepeticoes_acendeOPassoESuasRepeticoes', () => {
    const { fixture, el, host } = setup();
    host.selected.set('sb');
    host.repeats.set(true);
    fixture.detectChanges();

    expect(el.querySelector('#flowchart-sb-1')!.classList).toContain('is-selected');
    expect(el.querySelector('#flowchart-sb_r1-2')!.classList).toContain('is-selected');
    expect(el.querySelector('#flowchart-sa-0')!.classList).not.toContain('is-selected');
    expect(el.querySelector('#flowchart-sb-1')!.getAttribute('aria-pressed')).toBe('true');
  });

  // WEB-STG-007
  it('reveal_trazParaAVistaOPassoQueEstaFora', () => {
    const { fixture, viewport, el, host } = setup();
    host.stage().actualSize();
    fixture.detectChanges();
    const node = el.querySelector('#flowchart-sb-1')!;
    const inside = () => {
      const box = node.getBoundingClientRect();
      const rect = viewport.getBoundingClientRect();
      return box.top >= rect.top && box.bottom <= rect.bottom;
    };
    expect(inside()).toBeFalse();

    host.stage().reveal('sb');
    fixture.detectChanges();
    expect(inside()).toBeTrue();
  });

  // WEB-STG-008
  it('textoNovo_naoPerdeOZoom', () => {
    const { fixture, el, content, host } = setup();
    (el.querySelector('[aria-label="Aproximar"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const zoomed = content().style.transform;

    host.svg.set(SVG.replace('Passo A', 'Passo A mudado'));
    fixture.detectChanges();

    expect(content().style.transform).toBe(zoomed);
    expect(content().textContent).toContain('Passo A mudado');
  });

  // WEB-STG-009
  it('enterNoPasso_escolhe', () => {
    const { el, host } = setup();
    el.querySelector('#flowchart-sa-0')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

    expect(host.picks[0]?.id).toBe('sa');
  });
});
