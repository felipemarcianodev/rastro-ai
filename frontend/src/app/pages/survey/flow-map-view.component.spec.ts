import { Component, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAP_FIXTURE, MAP_FIXTURE_GRAPH } from '../../core/graph/flow-map-fixture.spec';
import { LayoutDirection } from '../../core/graph/layout';
import { FlowSummary } from '../../core/graph/summary';
import { FlowMap, SourceLanguage, SurveyDetail, SurveyStatus } from '../../core/models';
import { FlowMapViewComponent } from './flow-map-view.component';

const detail: SurveyDetail = {
  survey: {
    id: 's1',
    repository: 'pedidos-web',
    solutionPath: 'a.sln',
    language: SourceLanguage.DotNet,
    entryPoint: 'PedidosController.Criar',
    maxDepth: 5,
    commitHash: 'abc',
    status: SurveyStatus.Completed,
    schemaVersion: 1,
    nodeCount: 6,
    edgeCount: 3,
    unresolvedCount: 0,
    createdAt: '2026-11-04T10:00:00Z',
    crossRepositories: [],
    segments: [],
  },
  graph: MAP_FIXTURE_GRAPH,
};

@Component({
  imports: [FlowMapViewComponent],
  template: `<app-flow-map-view [detail]="detail" [selectedId]="selected()" (selectNode)="picked.push($event)" (directionChange)="directions.push($event)" (summaryChange)="summaries.push($event)" (focusFlow)="focused.push($event)" />`,
})
class HostComponent {
  readonly detail = detail;
  readonly selected = signal<string | null>(null);
  readonly picked: (string | null)[] = [];
  readonly directions: LayoutDirection[] = [];
  readonly summaries: FlowSummary[] = [];
  readonly focused: string[] = [];
}

async function setup(map: FlowMap | { status: number } = MAP_FIXTURE) {
  TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideHttpClient(), provideHttpClientTesting()] });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  const request = TestBed.inject(HttpTestingController).expectOne('/api/surveys/s1/map');
  if ('status' in map) request.flush({ code: 'NOT_FOUND' }, { status: map.status, statusText: 'Not Found' });
  else request.flush(map);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const block = (id: string) => el.querySelector<HTMLButtonElement>(`[data-block-id="${id}"]`)!;
  const pathButton = (text: string) => Array.from(el.querySelectorAll<HTMLButtonElement>('.map-path')).find((b) => b.textContent!.includes(text))!;
  return { fixture, el, host: fixture.componentInstance, block, pathButton };
}

describe('FlowMapViewComponent (Fase 31)', () => {
  // WEB-MAPV-001
  it('desenha_faixasNaOrdemEBlocosNaFaixaCerta', async () => {
    const { el, block } = await setup();

    expect(Array.from(el.querySelectorAll('.map-lane-title')).map((t) => t.textContent!.trim())).toEqual(['pedidos-web', 'api-pagamentos', 'Fora do código']);
    const left = (id: string) => parseInt(block(id).style.left, 10);
    expect(left('b1')).toBe(left('b3'));
    expect(left('b4')).toBeGreaterThan(left('b3'));
    expect(left('b5')).toBeGreaterThan(left('b4'));
    expect(block('b2').textContent).toContain('Se pagamento.Aprovado');
    expect(block('b1').textContent).toContain('Criar');
    // Letra e cor: o selo de cada tipo leva a letra.
    expect(block('b2').querySelector('.kind-glyph')!.textContent).toBe('?');
    expect(block('b5').classList).toContain('kind-outside');
  });

  // WEB-MAPV-002
  it('semCaminhoEscolhido_nadaApaga', async () => {
    const { el, pathButton } = await setup();

    expect(pathButton('Todos').getAttribute('aria-pressed')).toBe('true');
    expect(el.querySelectorAll('.map-block.is-dim').length).toBe(0);
    expect(el.querySelectorAll('.map-link.is-dim').length).toBe(0);
  });

  // WEB-MAPV-003
  it('caminhoEscolhido_acendeSoOsBlocosEAsLigacoesDele', async () => {
    const { fixture, el, block, pathButton } = await setup();

    pathButton('Se não').click();
    fixture.detectChanges();

    expect(pathButton('Se não').textContent).toContain('(falha)');
    expect(Array.from(el.querySelectorAll('.map-block.is-lit')).map((b) => b.getAttribute('data-block-id'))).toEqual(['b1', 'b2']);
    expect(block('b3').classList).toContain('is-dim');
    expect(el.querySelectorAll('.map-link.is-lit').length).toBe(1);
    expect(el.querySelectorAll('.map-link.is-dim').length).toBe(3);

    pathButton('Todos').click();
    fixture.detectChanges();
    expect(el.querySelectorAll('.map-block.is-dim').length).toBe(0);
  });

  // WEB-MAPV-004
  it('cliqueNoDesfecho_escolheOPrimeiroCaminhoComAquelaEscolha', async () => {
    const { fixture, el, pathButton } = await setup();

    el.querySelector<HTMLButtonElement>('.map-label.is-outcome')!.click();
    fixture.detectChanges();

    expect(pathButton('Se sim').getAttribute('aria-pressed')).toBe('true');
    expect(pathButton('Todos').getAttribute('aria-pressed')).toBe('false');
  });

  // WEB-MAPV-005
  it('cliqueNoDesfechoDoPainel_escolheOCaminhoDaDecisao', async () => {
    const { fixture, el, block, pathButton } = await setup();

    block('b2').click();
    fixture.detectChanges();
    const outcomes = Array.from(el.querySelectorAll<HTMLButtonElement>('.map-panel .map-outcomes .map-step'));
    expect(outcomes.length).toBe(2);
    expect(outcomes[1].textContent).toContain('falha');
    outcomes[1].click();
    fixture.detectChanges();

    expect(pathButton('Se não').getAttribute('aria-pressed')).toBe('true');
  });

  // WEB-MAPV-006
  it('cliqueNoBloco_escolheONoDoGrafoEMostraOsPassosQueEleRecolhe', async () => {
    const { fixture, el, host, block } = await setup();

    block('b1').click();
    fixture.detectChanges();

    expect(host.picked).toEqual(['n1']);
    expect(block('b1').classList).toContain('is-selected');
    const steps = Array.from(el.querySelectorAll('.map-panel .map-steps li'));
    expect(steps.map((li) => li.firstElementChild!.firstChild!.textContent)).toEqual(['Criar', 'Validar']);
    expect(steps.map((li) => li.querySelector('.map-where')!.textContent)).toEqual(['Pedidos.cs:10', 'Validator.cs:7']);
  });

  // WEB-MAPV-007
  it('painelDoBloco_listaAsChamadasQueSaemComVerboERota', async () => {
    const { fixture, el, host, block } = await setup();

    block('b3').click();
    fixture.detectChanges();
    const call = el.querySelector<HTMLButtonElement>('.map-panel .map-outcomes .map-step')!;
    expect(call.textContent).toContain('POST api/estornos');

    call.click();
    fixture.detectChanges();
    expect(host.picked.at(-1)).toBe('n5');
    expect(block('b4').classList).toContain('is-selected');
  });

  // WEB-MAPV-008
  it('anteriorESeguinte_andamNoCaminhoEscolhido', async () => {
    const { fixture, el, host, block, pathButton } = await setup();

    pathButton('Se sim').click();
    block('b2').click();
    fixture.detectChanges();
    const buttons = Array.from(el.querySelectorAll<HTMLButtonElement>('.map-panel-foot .btn'));
    const [previous, next] = buttons;

    next.click();
    fixture.detectChanges();
    expect(host.picked.at(-1)).toBe('n4');
    expect(block('b3').classList).toContain('is-selected');

    previous.click();
    fixture.detectChanges();
    expect(host.picked.at(-1)).toBe('n3');
  });

  // WEB-MAPV-009
  it('setasDoTeclado_andamPeloCaminhoENaPontaOBotaoFicaDesligado', async () => {
    const { fixture, el, host, block } = await setup();

    block('b1').click();
    fixture.detectChanges();
    const previous = el.querySelector<HTMLButtonElement>('.map-panel-foot .btn')!;
    expect(previous.disabled).toBeTrue();

    block('b1').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    expect(host.picked.at(-1)).toBe('n3');
    block('b2').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    fixture.detectChanges();
    expect(host.picked.at(-1)).toBe('n1');
  });

  // WEB-MAPV-010
  it('selecaoDeFora_acendeOBlocoQueRecolheOPasso', async () => {
    const { fixture, host, block } = await setup();

    host.selected.set('n2');
    fixture.detectChanges();
    expect(block('b1').classList).toContain('is-selected');

    host.selected.set(null);
    fixture.detectChanges();
    expect(block('b1').classList).not.toContain('is-selected');
  });

  // WEB-MAPV-011
  it('verSoEsteTrecho_entregaONoDoBlocoParaORecorte', async () => {
    const { fixture, el, host, block } = await setup();

    block('b3').click();
    fixture.detectChanges();
    const focus = Array.from(el.querySelectorAll<HTMLButtonElement>('.map-panel-foot .btn')).find((b) => b.textContent!.includes('Ver só este trecho'))!;
    focus.click();

    expect(host.focused).toEqual(['n4']);
  });

  // WEB-MAPV-012
  it('botaoDoDesenho_voltaAoHorizontalEOResumoVaiParaOPainel', async () => {
    const { el, host } = await setup();

    (el.querySelector('[aria-label^="Desenho em mapa"]') as HTMLButtonElement).click();
    expect(host.directions).toEqual([LayoutDirection.Horizontal]);
    expect(host.summaries.length).toBeGreaterThan(0);
  });

  // WEB-MAPV-013
  it('caminhosCortados_avisaQueHaMais', async () => {
    const { el } = await setup({ ...MAP_FIXTURE, pathsTruncated: true });

    expect(el.querySelector('.map-truncated')!.textContent).toContain('mais caminhos');
  });

  // WEB-MAPV-014
  it('erroDaApi_mostraAMensagemPeloCodigoETentaDeNovo', async () => {
    const { el } = await setup({ status: 404 });

    expect(el.querySelector('[role="alert"]')!.textContent).toContain('Não encontrado.');
    expect(el.querySelector('.map-block')).toBeNull();
  });

  // WEB-MAPV-015
  it('mapaVazio_explicaEmVezDeDesenharNada', async () => {
    const { el } = await setup({ ...MAP_FIXTURE, lanes: [], blocks: [], links: [], paths: [] });

    expect(el.textContent).toContain('O mapa deste fluxo está vazio');
    expect(el.querySelector('.map-path')).toBeNull();
  });

  // WEB-MAPV-016
  it('carregando_mostraOAndamentoAteOMapaChegar', () => {
    TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Montando o mapa do fluxo');
    TestBed.inject(HttpTestingController).expectOne('/api/surveys/s1/map');
  });
});
