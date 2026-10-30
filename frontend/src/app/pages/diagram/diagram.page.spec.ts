import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { consolidatedMermaid, stepId } from '../../core/graph/consolidated';
import { DIAGRAM_RENDERER, DiagramRenderer } from '../../core/mermaid-render';
import { EdgeConfidence, FlowGraph, NodeKind, SourceLanguage, SurveyDetail, SurveyDiagram, SurveyStatus } from '../../core/models';
import { DiagramPage } from './diagram.page';

const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'OrderJob.Execute',
  entryNodeId: 'job',
  nodes: [
    { id: 'job', kind: NodeKind.Entry, label: 'OrderJob.Execute' },
    { id: 'if', kind: NodeKind.Decision, label: 'if (x)' },
    { id: 'a', kind: NodeKind.Method, label: 'X.A' },
  ],
  edges: [
    { from: 'job', to: 'if', order: 0, confidence: EdgeConfidence.Extracted },
    { from: 'if', to: 'a', order: 0, label: 'then', confidence: EdgeConfidence.Extracted },
  ],
};

const detail = (commit = 'abcdef1234567'): SurveyDetail => ({
  survey: {
    id: 's1',
    repository: 'repo',
    solutionPath: 'a.sln',
    language: SourceLanguage.DotNet,
    entryPoint: 'OrderJob.Execute',
    maxDepth: 5,
    commitHash: commit,
    status: SurveyStatus.Completed,
    schemaVersion: 1,
    nodeCount: 3,
    edgeCount: 2,
    unresolvedCount: 0,
    createdAt: '2026-10-30T10:00:00Z',
    crossRepositories: [],
    segments: [],
  },
  graph,
});

// O Mermaid de verdade é lento e testado à parte (mermaid-render.spec): aqui o desenho é um SVG falso
// com o mesmo formato de id que o Mermaid dá ao g.node.
function svgOf(text: string): string {
  const ids = [...text.matchAll(/^\s*(s[0-9a-z]+(?:_r\d+)?)[[({>]/gm)].map((m) => m[1]);
  return `<svg viewBox="0 0 100 100">${ids.map((id, i) => `<g class="node" id="flowchart-${id}-${i}"><rect fill="#ffffff" stroke="#b7c1bc"/><text>${id}</text></g>`).join('')}</svg>`;
}

function render(renderer: DiagramRenderer = (text) => Promise.resolve(svgOf(text))) {
  TestBed.configureTestingModule({
    imports: [DiagramPage],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: DIAGRAM_RENDERER, useValue: renderer }],
  });
  const fixture = TestBed.createComponent(DiagramPage);
  fixture.componentRef.setInput('id', 's1');
  const http = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
  return { fixture, http, el: fixture.nativeElement as HTMLElement };
}

function answer(http: HttpTestingController, saved: SurveyDiagram | null, survey = detail()): void {
  http.expectOne('/api/surveys/s1').flush(survey);
  const request = http.expectOne('/api/surveys/s1/diagram');
  if (saved) request.flush(saved);
  else request.flush({ code: 'NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
}

const wait = (ms = 30) => new Promise((resolve) => setTimeout(resolve, ms));
const button = (el: HTMLElement, text: string) => Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes(text)) as HTMLButtonElement;
const editor = (el: HTMLElement) => el.querySelector('textarea') as HTMLTextAreaElement;
function type(el: HTMLElement, value: string): void {
  const area = editor(el);
  area.value = value;
  area.dispatchEvent(new Event('input'));
}

async function loaded(saved: SurveyDiagram | null = null, survey = detail(), renderer?: DiagramRenderer) {
  const ctx = render(renderer);
  answer(ctx.http, saved, survey);
  ctx.fixture.detectChanges();
  await wait();
  ctx.fixture.detectChanges();
  return ctx;
}

describe('DiagramPage (Fase 27)', () => {
  // WEB-DIA-010
  it('semDiagramaSalvo_geraDoGrafoEDesenhaAPrevia', async () => {
    const { el } = await loaded();

    expect(editor(el).value).toContain('flowchart TD');
    expect(editor(el).value).toContain(`${stepId('if')}{"Se x"}`);
    expect(el.textContent).toContain('Gerada do fluxo, ainda não salva');
    expect(el.querySelectorAll('.preview g.node').length).toBe(3);
  });

  // WEB-DIA-011
  it('comDiagramaSalvo_mostraOTextoSalvoEAvisaQuandoOCommitMudou', async () => {
    const { el } = await loaded({ mermaid: 'flowchart TD\n  sa["Meu texto"]\n', commitHash: '1111111aaaa', updatedAt: '2026-10-30T10:00:00Z' });

    expect(editor(el).value).toContain('Meu texto');
    expect(el.textContent).toContain('Editado no commit');
    expect(el.textContent).toContain('1111111');
    expect(el.textContent).toContain('o levantamento é do commit');
  });

  // WEB-DIA-012
  it('salvar_enviaOTextoDoEditorEMostraAConfirmacao', async () => {
    const { fixture, http, el } = await loaded();
    type(el, 'flowchart TD\n  sx["meu texto"]\n');
    fixture.detectChanges();
    const text = editor(el).value;

    button(el, 'Salvar').click();
    const request: TestRequest = http.expectOne('/api/surveys/s1/diagram');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ mermaid: text });
    request.flush({ mermaid: text, commitHash: 'abcdef1234567', updatedAt: '2026-10-30T10:01:00Z' });
    fixture.detectChanges();

    expect(el.textContent).toContain('Diagrama salvo.');
    expect(el.textContent).toContain('Salva');
  });

  // WEB-DIA-013
  it('salvar_apiRecusa_mostraAMensagemDoCodigoDeErro', async () => {
    const { fixture, http, el } = await loaded();
    type(el, 'flowchart TD\n  sx["meu texto"]\n');
    fixture.detectChanges();

    button(el, 'Salvar').click();
    http.expectOne('/api/surveys/s1/diagram').flush({ code: 'DIAGRAM_INVALID' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(el.textContent).toContain('O diagrama está vazio ou grande demais.');
    // WEB-DIA-030: o erro não some sozinho e usa role=alert.
    expect(el.querySelector('.toast')!.getAttribute('role')).toBe('alert');
    await wait(4300);
    fixture.detectChanges();
    expect(el.textContent).toContain('O diagrama está vazio ou grande demais.');
  });

  // WEB-DIA-014
  it('digitar_desenhaSoDepoisDe300msDaUltimaTecla', async () => {
    const calls: string[] = [];
    const { fixture, el } = await loaded(null, detail(), (text) => {
      calls.push(text);
      return Promise.resolve(svgOf(text));
    });
    calls.length = 0;

    const area = editor(el);
    area.value = 'flowchart TD\n  sx["um"]\n';
    area.dispatchEvent(new Event('input'));
    area.value = 'flowchart TD\n  sx["dois"]\n';
    area.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await wait(150);
    expect(calls.length).toBe(0);
    await wait(250);

    expect(calls).toEqual(['flowchart TD\n  sx["dois"]\n']);
  });

  // WEB-DIA-015
  it('erroDeSintaxe_mostraAMensagemEMantemOUltimoDesenho', async () => {
    let fail = false;
    const { fixture, el } = await loaded(null, detail(), (text) => (fail ? Promise.reject(new Error('Parse error on line 2')) : Promise.resolve(svgOf(text))));
    fail = true;

    const area = editor(el);
    area.value = 'flowchart TD\n  sx[\n';
    area.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await wait(400);
    fixture.detectChanges();

    expect(el.querySelector('.render-error')!.textContent).toContain('Parse error on line 2');
    expect(el.textContent).toContain('A prévia abaixo é a última que deu certo.');
    expect(el.querySelectorAll('.preview g.node').length).toBe(3);
  });

  // WEB-DIA-016
  it('clicarNoPasso_abrePainelDeCorEOsBotoesReescrevemOTexto', async () => {
    const { fixture, el } = await loaded();
    const id = stepId('if');

    (el.querySelector(`.preview g.node[id^="flowchart-${id}-"]`) as SVGElement).dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    expect(el.querySelector('.paint')).not.toBeNull();
    expect(el.querySelector('.paint-title')!.textContent).toContain(id);

    const fill = el.querySelector('#paint-fill') as HTMLInputElement;
    fill.value = '#ff0000';
    fill.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    button(el, 'Só este passo').click();
    fixture.detectChanges();
    expect(editor(el).value).toContain(`style ${id} fill:#ff0000`);

    button(el, 'Todos do tipo').click();
    fixture.detectChanges();
    expect(editor(el).value).toContain('classDef decision fill:#ff0000');
    // Desfazer volta o texto de antes do último botão.
    button(el, 'Desfazer').click();
    fixture.detectChanges();
    expect(editor(el).value).not.toContain('classDef decision fill:#ff0000');
    expect(editor(el).value).toContain(`style ${id} fill:#ff0000`);
  });

  // WEB-DIA-017
  it('gerarDeNovo_pedeConfirmacaoNoDialogoDoTemaEMantemAEdicao', async () => {
    const { fixture, el } = await loaded({ mermaid: 'flowchart TD\n  sa["x"]\n  style sgone fill:#ff0000\n', commitHash: 'abcdef1234567', updatedAt: '2026-10-30T10:00:00Z' });

    button(el, 'Gerar de novo').click();
    fixture.detectChanges();
    const dialog = el.querySelector('.overlay .dialog')!;
    expect(dialog.textContent).toContain('1 passo editado saiu porque não existe mais no código.');

    (dialog.querySelector('.btn-p') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('.overlay')).toBeNull();
    expect(editor(el).value).toContain(`${stepId('if')}{"Se x"}`);
    expect(editor(el).value).not.toContain('sgone');
  });

  // WEB-DIA-018
  it('descartarEdicao_apagaNaApiEVoltaAoGerado', async () => {
    const { fixture, http, el } = await loaded({ mermaid: 'flowchart TD\n  sa["x"]\n', commitHash: 'abcdef1234567', updatedAt: '2026-10-30T10:00:00Z' });

    button(el, 'Descartar edição').click();
    fixture.detectChanges();
    (el.querySelector('.overlay .btn-danger') as HTMLButtonElement).click();
    const request = http.expectOne('/api/surveys/s1/diagram');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(editor(el).value).toContain(`${stepId('if')}{"Se x"}`);
    expect(el.textContent).toContain('Gerada do fluxo, ainda não salva');
  });

  // WEB-DIA-020
  it('diagramaHerdado_mesclaComABaselineDoLevantamentoAnteriorEAvisa', async () => {
    const old = detail();
    old.survey.id = 'old';
    old.graph = { ...graph, nodes: graph.nodes.map((n) => (n.id === 'a' ? { ...n, label: 'X.OldA' } : n)) };
    const baseline = consolidatedMermaid(old.graph);
    const mermaid = baseline.replace('"Se x"', '"Minha decisão"') + '  style sgone fill:#ff0000\n';
    const { fixture, http, el } = render();

    http.expectOne('/api/surveys/s1').flush(detail());
    http.expectOne('/api/surveys/s1/diagram').flush({ mermaid, commitHash: '1111111aaaa', updatedAt: '2026-10-30T10:00:00Z', fromSurveyId: 'old' });
    http.expectOne('/api/surveys/old').flush(old);
    await wait();
    fixture.detectChanges();

    expect(editor(el).value).toContain(`${stepId('if')}{"Minha decisão"}`);
    expect(editor(el).value).toContain(`${stepId('a')}["A"]`);
    expect(editor(el).value).not.toContain('OldA');
    expect(el.textContent).toContain('Edição trazida do levantamento anterior.');
    expect(el.textContent).toContain('1 passo editado saiu porque não existe mais no código.');
    expect(el.textContent).toContain('Alterações não salvas');
    expect(button(el, 'Salvar').disabled).toBeFalse();
  });

  // WEB-DIA-021
  it('diagramaHerdado_levantamentoAnteriorNaoCarrega_mesclaSemBaseline', async () => {
    const mermaid = 'flowchart TD\n  ' + stepId('a') + '["Texto antigo"]\n';
    const { fixture, http, el } = render();

    http.expectOne('/api/surveys/s1').flush(detail());
    http.expectOne('/api/surveys/s1/diagram').flush({ mermaid, updatedAt: '2026-10-30T10:00:00Z', fromSurveyId: 'old' });
    http.expectOne('/api/surveys/old').flush({ code: 'NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    await wait();
    fixture.detectChanges();

    expect(editor(el).value).toContain(`${stepId('a')}["Texto antigo"]`);
    expect(el.textContent).toContain('Edição trazida do levantamento anterior.');
    expect(el.textContent).not.toContain('saiu porque');
  });

  // WEB-DIA-019
  it('levantamentoSemFluxoPronto_mostraAvisoEVoltar', () => {
    const { http, fixture, el } = render();
    const pending = detail();
    pending.survey.status = SurveyStatus.Running;
    delete pending.graph;
    answer(http, null, pending);
    fixture.detectChanges();

    expect(el.querySelector('.notice.is-error')!.textContent).toContain('ainda não tem um fluxo pronto');
  });

  // WEB-DIA-031
  it('passoNaPrevia_alcancavelPorTeclado_enterEEspacoEscolhemOPasso', async () => {
    const { fixture, el } = await loaded();
    const node = el.querySelector(`.preview g.node[id^="flowchart-${stepId('if')}-"]`) as SVGElement;

    expect(node.getAttribute('tabindex')).toBe('0');
    expect(node.getAttribute('role')).toBe('button');
    expect(node.getAttribute('aria-label')).toContain('Passo: ');
    node.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges();
    expect(el.querySelector('.paint')).not.toBeNull();

    (button(el, 'Fechar') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('.paint')).toBeNull();
    node.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
    fixture.detectChanges();
    expect(el.querySelector('.paint')).not.toBeNull();
  });

  // WEB-DIA-032
  it('zoom_mudaAEscalaDoSvgSemDesenharDeNovo', async () => {
    const calls: string[] = [];
    const { fixture, el } = await loaded(null, detail(), (text) => {
      calls.push(text);
      return Promise.resolve(svgOf(text));
    });
    const svg = () => el.querySelector('.preview svg') as SVGElement;
    const before = calls.length;

    expect(svg().style.width).toBe('100%');
    button(el, '+').click();
    fixture.detectChanges();
    expect(svg().style.width).toBe('125px');
    button(el, '100%').click();
    fixture.detectChanges();
    expect(svg().style.width).toBe('100px');
    button(el, '−').click();
    fixture.detectChanges();
    expect(svg().style.width).toBe('75px');
    el.querySelector('.preview')!.dispatchEvent(new WheelEvent('wheel', { ctrlKey: true, deltaY: -100, bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(svg().style.width).toBe('100px');
    button(el, 'Ajustar').click();
    fixture.detectChanges();
    expect(svg().style.width).toBe('100%');
    expect(calls.length).toBe(before);
  });

  // WEB-DIA-033
  it('gerarDeNovo_usaOTomPrimario', async () => {
    const { fixture, el } = await loaded({ mermaid: 'flowchart TD\n  sa["x"]\n', commitHash: 'abcdef1234567', updatedAt: '2026-10-30T10:00:00Z' });

    button(el, 'Gerar de novo').click();
    fixture.detectChanges();

    expect(el.querySelector('.overlay .btn-p')).not.toBeNull();
    expect(el.querySelector('.overlay .btn-danger')).toBeNull();
  });

  // WEB-DIA-034
  it('salvar_desabilitadoQuandoNadaSalvoEOTextoEIgualAoGerado', async () => {
    const { fixture, el } = await loaded();

    expect(button(el, 'Salvar').disabled).toBeTrue();
    type(el, 'flowchart TD\n  sx["meu texto"]\n');
    fixture.detectChanges();
    expect(button(el, 'Salvar').disabled).toBeFalse();
  });
});
