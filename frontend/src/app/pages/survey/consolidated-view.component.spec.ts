import { Component, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { stepId } from '../../core/graph/consolidated';
import { LayoutDirection } from '../../core/graph/layout';
import { DIAGRAM_RENDERER, DiagramRenderer } from '../../core/mermaid-render';
import { EdgeConfidence, FlowGraph, NodeKind, SourceLanguage, SurveyDetail, SurveyStatus } from '../../core/models';
import { ConsolidatedViewComponent } from './consolidated-view.component';

const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'OrderJob.Execute',
  entryNodeId: 'job',
  nodes: [
    { id: 'job', kind: NodeKind.Entry, label: 'OrderJob.Execute' },
    { id: 'if', kind: NodeKind.Decision, label: 'if (x)' },
    { id: 'a', kind: NodeKind.Method, label: 'X.A' },
    // Método de passagem: não vira passo no desenho.
    { id: 'pass', kind: NodeKind.Method, label: 'X.Pass' },
    { id: 'b', kind: NodeKind.Method, label: 'X.B' },
  ],
  edges: [
    { from: 'job', to: 'if', order: 0, confidence: EdgeConfidence.Extracted },
    { from: 'if', to: 'a', order: 0, label: 'then', confidence: EdgeConfidence.Extracted },
    { from: 'job', to: 'pass', order: 1, confidence: EdgeConfidence.Extracted },
    { from: 'pass', to: 'b', order: 0, confidence: EdgeConfidence.Extracted },
  ],
};

const detail: SurveyDetail = {
  survey: {
    id: 's1',
    repository: 'repo',
    solutionPath: 'a.sln',
    language: SourceLanguage.DotNet,
    entryPoint: 'OrderJob.Execute',
    maxDepth: 5,
    commitHash: 'abc',
    status: SurveyStatus.Completed,
    schemaVersion: 1,
    nodeCount: 5,
    edgeCount: 4,
    unresolvedCount: 0,
    createdAt: '2026-10-30T10:00:00Z',
    crossRepositories: [],
    segments: [],
  },
  graph,
};

// SVG falso com o formato de id do Mermaid (flowchart-<id>-<n>), um g.node por passo do texto.
function svgOf(text: string): string {
  const ids = [...text.matchAll(/^\s*(s[0-9a-z]+(?:_r\d+)?)[[({>]/gm)].map((m) => m[1]);
  return `<svg viewBox="0 0 100 100">${ids.map((id, i) => `<g class="node" id="flowchart-${id}-${i}"><rect/><text>${id}</text></g>`).join('')}</svg>`;
}

@Component({
  imports: [ConsolidatedViewComponent],
  template: `<app-consolidated-view [detail]="detail" [focus]="focus()" [selectedId]="selected()" (selectNode)="picked.push($event)" (directionChange)="directions.push($event)" />`,
})
class HostComponent {
  readonly detail = detail;
  readonly selected = signal<string | null>(null);
  readonly focus = signal<FlowGraph | null>(null);
  readonly picked: (string | null)[] = [];
  readonly directions: LayoutDirection[] = [];
}

const wait = () => new Promise((resolve) => setTimeout(resolve, 20));

async function setup(saved: string | null = null, renderer?: DiagramRenderer) {
  const texts: string[] = [];
  const fake: DiagramRenderer = (text) => {
    texts.push(text);
    return Promise.resolve(svgOf(text));
  };
  TestBed.configureTestingModule({
    imports: [HostComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: DIAGRAM_RENDERER, useValue: renderer ?? fake }],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  const request = TestBed.inject(HttpTestingController).expectOne('/api/surveys/s1/diagram');
  if (saved) request.flush({ mermaid: saved, updatedAt: '2026-10-30T10:00:00Z' });
  else request.flush({ code: 'NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
  fixture.detectChanges();
  await wait();
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement, host: fixture.componentInstance, texts };
}

function pointerClick(target: Element): void {
  target.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, button: 0, pointerId: 1, clientX: 5, clientY: 5 }));
  target.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, button: 0, pointerId: 1, clientX: 5, clientY: 5 }));
}

describe('ConsolidatedViewComponent (Fase 28)', () => {
  // WEB-CVW-001
  it('semEdicaoSalva_desenhaOGeradoEOCliqueEscolheONoDoGrafo', async () => {
    const { el, host } = await setup();

    expect(el.textContent).toContain('Gerada do fluxo.');
    pointerClick(el.querySelector(`g.node[id^="flowchart-${stepId('if')}-"]`)!);
    expect(host.picked).toEqual(['if']);
  });

  // WEB-CVW-002
  it('comEdicaoSalva_desenhaOTextoDoDono', async () => {
    const saved = `flowchart TD\n  ${stepId('job')}(["Começo do dono"])\n  ${stepId('a')}["Meu passo"]\n`;
    const { el, texts } = await setup(saved);

    expect(texts.at(-1)).toBe(saved);
    expect(el.textContent).toContain('Com as suas cores e textos.');
    expect(el.querySelectorAll('g.node').length).toBe(2);
  });

  // WEB-CVW-003
  it('selecaoDeFora_acendeOPassoEAvisaQuandoElePassaSemPasso', async () => {
    const { fixture, el, host } = await setup();

    host.selected.set('a');
    fixture.detectChanges();
    expect(el.querySelector(`g.node[id^="flowchart-${stepId('a')}-"]`)!.classList).toContain('is-selected');
    expect(el.textContent).not.toContain('não aparece neste desenho');

    host.selected.set('pass');
    fixture.detectChanges();
    expect(el.textContent).toContain('não aparece neste desenho');
  });

  // WEB-CVW-004
  it('botaoDoDesenho_voltaAoHorizontalELinkLevaAEdicao', async () => {
    const { el, host } = await setup();

    (el.querySelector('[aria-label^="Desenho consolidado"]') as HTMLButtonElement).click();
    expect(host.directions).toEqual([LayoutDirection.Horizontal]);
    const link = Array.from(el.querySelectorAll('a')).find((a) => a.textContent?.includes('Editar texto e cores'))!;
    expect(link.getAttribute('href')).toBe('/surveys/s1/visao');
  });

  // WEB-CVW-005
  it('textoSalvoQueNaoDesenha_mandaCorrigirNaEdicao', async () => {
    const { el } = await setup('flowchart TD\n  x[', () => Promise.reject(new Error('Parse error')));

    expect(el.querySelector('[role="alert"]')!.textContent).toContain('não desenha');
  });

  // WEB-CVW-006
  it('recorte_desenhaSoOsPassosDoRecorteComACorDoDono', async () => {
    const saved = `flowchart TD
  ${stepId('job')}(["Começo"])
  ${stepId('a')}["A"]
  ${stepId('b')}["B"]
  style ${stepId('a')} fill:#ff0000
`;
    const { fixture, host, texts } = await setup(saved);

    host.focus.set({ ...graph, nodes: graph.nodes.filter((n) => n.id !== 'b' && n.id !== 'pass'), edges: graph.edges.slice(0, 2) });
    fixture.detectChanges();
    await wait();

    expect(texts.at(-1)).toContain(`style ${stepId('a')} fill:#ff0000`);
    expect(texts.at(-1)).not.toContain(`${stepId('b')}[`);
  });
});
