import { Component, DebugElement, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AiStatusService } from '../../core/ai-status.service';
import { DesktopBridge } from '../../core/desktop-bridge.service';
import { MAP_FIXTURE_GRAPH } from '../../core/graph/flow-map-fixture.spec';
import { LayoutDirection } from '../../core/graph/layout';
import { FlowSummary } from '../../core/graph/summary';
import { AiProvider, AiUse, BusinessFlow, BusinessKind, BusinessLayout, BusinessStep, LabelOrigin, LabelState, RepositoryHostKind, RepositoryLinks, SourceLanguage, SurveyDetail, SurveyStatus } from '../../core/models';
import { businessLayout } from '../../core/graph/business-layout';
import { By } from '@angular/platform-browser';
import { FCanvasComponent, FConnectionContent, FConnectionWaypoints, FControlSchemeController, FDraggableDirective, FNodeDirective, FZoomDirective } from '@foblex/flow';
import { BRANCH_LABEL_POSITION, BUSINESS_ORIGIN_BADGE_KEY, BusinessViewComponent, CLICK_TOLERANCE_PX, FEEDBACK_MS, KIND_DETECTED, SAVE_DEBOUNCE_MS, SAVED_MS, ZOOM_MAX, ZOOM_MIN } from './business-view.component';

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

function step(id: string, nodeId: string, kind: BusinessKind, label: string, extra: Partial<BusinessStep> = {}): BusinessStep {
  return {
    id,
    nodeId,
    kind,
    label,
    labelOrigin: LabelOrigin.Rule,
    labelState: LabelState.Confirmed,
    ref: { repository: 'pedidos-web', file: 'src/Pedidos.cs', line: 10, commitHash: 'abcdef1234567890', branch: 'main' },
    sourceNodeIds: [nodeId],
    inferred: false,
    codeChanged: false,
    ...extra,
  };
}

const FLOW: BusinessFlow = {
  surveyId: 's1',
  schemaVersion: 1,
  collapsedDecisions: 0,
  steps: [
    step('s1', 'n1', BusinessKind.Start, 'Pedido recebido', { sourceNodeIds: ['n1', 'n2'] }),
    step('s2', 'n3', BusinessKind.Decision, 'O pagamento foi aprovado?', { labelOrigin: LabelOrigin.Ai, labelState: LabelState.Proposed }),
    step('s3', 'n4', BusinessKind.Notification, 'Cliente é avisado do estorno', { inferred: true, ref: null }),
    step('s4', 'n5', BusinessKind.End, 'Pedido concluído'),
  ],
  edges: [
    { from: 's1', to: 's2', label: null, inferred: false },
    { from: 's2', to: 's3', label: 'else', inferred: false },
    { from: 's2', to: 's4', label: 'then', inferred: true },
  ],
};

const LINKS: RepositoryLinks[] = [
  {
    repository: 'pedidos-web',
    host: RepositoryHostKind.GitHub,
    commitTemplate: 'https://github.com/org/pedidos-web/blob/abcdef1/{path}#L{line}',
    branchTemplate: null,
    editorTemplate: 'vscode://file/C:/repos/pedidos-web/{path}:{line}',
  },
];

@Component({
  imports: [BusinessViewComponent],
  // O Foblex avisa (e não desenha) um fluxo sem altura: na página, o contêiner da visão dá a altura.
  styles: ['app-business-view { display: block; width: 900px; height: 600px; }'],
  template: `<app-business-view [detail]="detail" [selectedId]="selected()" [notes]="notes" (selectNode)="picked.push($event); selected.set($event)" (directionChange)="directions.push($event)" (summaryChange)="summaries.push($event)" (focusFlow)="focused.push($event)" (showInGraph)="inGraph.push($event)" />`,
})
class HostComponent {
  readonly detail = detail;
  readonly selected = signal<string | null>(null);
  readonly notes = new Map([['n3', 'Nota de teste do passo']]);
  readonly picked: (string | null)[] = [];
  readonly directions: LayoutDirection[] = [];
  readonly summaries: FlowSummary[] = [];
  readonly focused: string[] = [];
  readonly inGraph: string[] = [];
}

const NO_LAYOUT: BusinessLayout = { steps: [], edges: [] };
const LAYOUT_URL = '/api/surveys/s1/business/layout';
const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
// Espera o debounce de gravação passar.
const afterDebounce = () => wait(SAVE_DEBOUNCE_MS + 60);

async function setup(flow: BusinessFlow | { status: number } = FLOW, links: RepositoryLinks[] = LINKS, layout: BusinessLayout | { status: number } = NO_LAYOUT) {
  const opened: string[] = [];
  TestBed.configureTestingModule({
    imports: [HostComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DesktopBridge, useValue: { openExternal: (url: string) => { opened.push(url); return Promise.resolve(true); } } }],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  const http = TestBed.inject(HttpTestingController);
  const business = http.expectOne('/api/surveys/s1/business');
  const linksRequest = http.expectOne('/api/surveys/s1/links');
  const layoutRequest = http.expectOne(LAYOUT_URL);
  linksRequest.flush(links);
  if ('status' in layout) layoutRequest.flush({ code: 'ERRO' }, { status: layout.status, statusText: 'Erro' });
  else layoutRequest.flush(layout);
  if ('status' in flow) business.flush({ code: 'NOT_FOUND' }, { status: flow.status, statusText: 'Not Found' });
  else business.flush(flow);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const node = (id: string) => el.querySelector<HTMLElement>(`[data-step-id="${id}"]`)!;
  const button = (text: string) => Array.from(el.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent!.includes(text))!;
  return { fixture, el, host: fixture.componentInstance, node, button, opened, http };
}

async function setupLinksError() {
  TestBed.configureTestingModule({
    imports: [HostComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DesktopBridge, useValue: { openExternal: () => Promise.resolve(true) } }],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  const http = TestBed.inject(HttpTestingController);
  http.expectOne('/api/surveys/s1/links').flush({ code: 'ERRO' }, { status: 500, statusText: 'Erro' });
  http.expectOne(LAYOUT_URL).flush(NO_LAYOUT);
  http.expectOne('/api/surveys/s1/business').flush(FLOW);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return { fixture, el, node: (id: string) => el.querySelector<HTMLElement>(`[data-step-id="${id}"]`)! };
}

describe('BusinessViewComponent (Fase 57)', () => {
  // WEB-BIZV-001
  it('fluxoCarregado_desenhaUmBalaoPorPassoComAClasseDoTipo', async () => {
    const { node } = await setup();

    expect(node('s1').classList).toContain('biz-kind-start');
    expect(node('s2').classList).toContain('biz-kind-decision');
    expect(node('s3').classList).toContain('biz-kind-notification');
    expect(node('s4').classList).toContain('biz-kind-end');
    expect(node('s2').textContent).toContain('O pagamento foi aprovado?');
  });

  // WEB-BIZV-002
  it('decisao_desenhaOLosangoEOsOutrosTiposNao', async () => {
    const { node } = await setup();

    expect(node('s2').querySelector('.biz-shape polygon')).not.toBeNull();
    expect(node('s1').querySelector('.biz-shape')).toBeNull();
  });

  // WEB-BIZV-003
  it('balao_mostraAReferenciaOsSelosDePropostoEInferidoEOGlifo', async () => {
    const { node } = await setup();

    expect(node('s1').querySelector('.biz-ref')!.textContent).toBe('pedidos-web · Pedidos.cs:10');
    expect(node('s1').querySelector('.biz-ref')!.getAttribute('title')).toBe('pedidos-web · src/Pedidos.cs:10');
    expect(node('s1').getAttribute('title')).toBe('pedidos-web · src/Pedidos.cs:10');
    expect(node('s2').querySelector('.biz-badge.is-proposed')!.textContent).toContain('proposto');
    expect(node('s1').querySelector('.biz-badge.is-proposed')).toBeNull();
    expect(node('s3').querySelector('.biz-badge.is-inferred')!.textContent).toContain('inferido');
    expect(node('s3').querySelector('.biz-ref')).toBeNull();
    expect(node('s2').querySelector('.biz-glyph')!.textContent).toBe('?');
  });

  // WEB-BIZV-004
  it('ligacoes_desenhamUmaPorArestaComORotuloDoRamoEATracejadaDaInferida', async () => {
    const { el } = await setup();

    expect(el.querySelectorAll('f-connection').length).toBe(3);
    const labels = Array.from(el.querySelectorAll('.edge-label')).map((l) => l.textContent!.trim());
    // O rótulo da API vem em código (then, else) e a tela mostra em texto simples, como no grafo.
    expect(labels).toEqual(jasmine.arrayContaining(['Se sim', 'Se não']));
    expect(el.querySelectorAll('f-connection.edge-inferred').length).toBe(1);
  });

  // WEB-BIZV-025
  it('textoLongo_ganhaPontoDeQuebraDepoisDoPontoSemMudarOTexto', async () => {
    const longo = { ...FLOW, steps: [FLOW.steps[0], { ...FLOW.steps[1], label: 'SettingsController.SetLanguage' }, ...FLOW.steps.slice(2)] };
    const { node } = await setup(longo);

    const text = node('s2').querySelector('.biz-text')!;
    expect(text.textContent).toBe('SettingsController.SetLanguage');
    expect(text.querySelectorAll('wbr').length).toBe(2);
    expect(text.firstChild!.textContent).toBe('SettingsController.');
    expect(text.querySelector('wbr')!.previousSibling!.textContent).toBe('SettingsController.');
  });

  // WEB-BIZV-026
  // O Foblex não dá medida aos elementos no Karma (tudo sai com 0 x 0), então o teste confere a posição que o desenho pede.
  it('rotuloDoRamo_ficaPertoDoDestinoDaLigacao', async () => {
    const { fixture } = await setup();

    const contents = fixture.debugElement.queryAll(By.directive(FConnectionContent));

    expect(contents.length).toBe(2);
    const positions = contents.map((content) => content.injector.get(FConnectionContent).position());
    // As duas saem da mesma decisão e ficam perto da própria chegada, centradas sobre o destino.
    expect(positions).toEqual([BRANCH_LABEL_POSITION, BRANCH_LABEL_POSITION]);
  });

  // WEB-BIZV-081 (UX-FIM-213)
  it('rotuloDoRamo_naoPassaDaLarguraDoBalao_eLevaOTextoInteiroNaDica', async () => {
    const { el } = await setup();

    const label = el.querySelector<HTMLElement>('.biz-host .edge-label')!;
    const style = getComputedStyle(label);
    expect(style.maxWidth).toBe('200px');
    expect(style.textOverflow).toBe('ellipsis');
    expect(label.title).toBe(label.textContent!.trim());
  });

  // WEB-BIZV-027
  it('rotuloDoBalao_naoRepeteOTipoEIncluiOEstado', async () => {
    const flow = { ...FLOW, steps: [FLOW.steps[0], FLOW.steps[1], { ...FLOW.steps[2], label: 'Notificação ao cliente' }, { ...FLOW.steps[3], label: 'Fim' }] };
    const { node } = await setup(flow);

    expect(node('s1').getAttribute('aria-label')).toBe('Início: Pedido recebido');
    expect(node('s2').getAttribute('aria-label')).toBe('Decisão: O pagamento foi aprovado? (proposto, texto pela IA)');
    expect(node('s3').getAttribute('aria-label')).toBe('Notificação ao cliente (inferido)');
    expect(node('s4').getAttribute('aria-label')).toBe('Fim');
  });

  // WEB-BIZV-028
  it('anuncioDoPasso_eUmTextoOcultoCurtoEmRegiaoVivaEOTituloDoPainelNaoE', async () => {
    const { fixture, el, node } = await setup();
    const live = () => el.querySelector('.visually-hidden[aria-live="polite"]')!;

    expect(live().textContent).toBe('');
    node('s1').click();
    fixture.detectChanges();

    expect(live().textContent).toBe('Início: Pedido recebido');
    expect(el.querySelector('.biz-panel-title')!.hasAttribute('aria-live')).toBeFalse();
    expect(el.querySelectorAll('[aria-live]').length).toBe(1);
  });

  // WEB-BIZV-029
  it('rotaDeLinksComErro_avisaNoPainelEmVezDeSumirComOsBotoes', async () => {
    const { fixture, el, node } = await setupLinksError();

    node('s1').click();
    fixture.detectChanges();

    expect(el.querySelector('.biz-links-note')!.textContent).toContain('Links do repositório indisponíveis');
    expect(el.querySelector('.biz-open')!.children.length).toBe(0);
  });

  // WEB-BIZV-033
  it('dica_dizQueArrastarMoveATela', async () => {
    const { el } = await setup();

    expect(el.querySelector('.biz-hint')!.textContent).toContain('Arraste');
  });

  // WEB-BIZV-005
  it('cliqueNoBalao_escolheONoDoGrafoEAbreOPainelDoPasso', async () => {
    const { fixture, el, host, node } = await setup();

    node('s2').click();
    fixture.detectChanges();

    expect(host.picked).toEqual(['n3']);
    expect(node('s2').classList).toContain('is-selected');
    expect(node('s2').getAttribute('aria-pressed')).toBe('true');
    const panel = el.querySelector('.biz-panel')!;
    expect(panel.querySelector('.kind')!.textContent).toContain('Decisão');
    expect(panel.querySelector('.biz-panel-title')!.textContent).toBe('O pagamento foi aprovado?');
    expect(panel.querySelector('.biz-origin')!.textContent).toContain('Proposto pela IA');
  });

  // WEB-BIZV-006
  it('painel_mostraOCodigoComRepositorioArquivoLinhaCommitCurtoEBranch', async () => {
    const { fixture, el, node } = await setup();

    node('s1').click();
    fixture.detectChanges();

    const code = el.querySelector('.biz-code')!.textContent!;
    expect(code).toContain('pedidos-web');
    expect(code).toContain('src/Pedidos.cs');
    expect(code).toContain('10');
    expect(code).toContain('abcdef1');
    expect(code).not.toContain('abcdef12');
    expect(code).toContain('main');
    expect(el.querySelector('.biz-origin')!.textContent).toContain('Confirmado pela regra');
  });

  // WEB-BIZV-007
  it('abrirNoHostEAbrirNoEditor_entregamAUrlPreenchidaAPonteDaJanela', async () => {
    const { fixture, el, node, button, opened } = await setup();

    node('s1').click();
    fixture.detectChanges();
    expect(el.querySelector('.biz-open')!.textContent).toContain('Abrir no GitHub');
    button('Abrir no GitHub').click();
    button('Abrir no editor').click();

    expect(opened).toEqual(['https://github.com/org/pedidos-web/blob/abcdef1/src/Pedidos.cs#L10', 'vscode://file/C:/repos/pedidos-web/src/Pedidos.cs:10']);
  });

  // WEB-BIZV-008
  it('semModeloDeLink_osBotoesDeAbrirNaoAparecem', async () => {
    const { fixture, el, node } = await setup(FLOW, []);

    node('s1').click();
    fixture.detectChanges();

    expect(el.querySelector('.biz-open')!.children.length).toBe(0);
    expect(el.querySelector('.biz-code')).not.toBeNull();
  });

  // WEB-BIZV-009
  it('passoSemReferencia_dizQueNaoTemCodigoENaoMostraBotoes', async () => {
    const { fixture, el, node } = await setup();

    node('s3').click();
    fixture.detectChanges();

    expect(el.querySelector('.biz-code')).toBeNull();
    expect(el.querySelector('.biz-none')!.textContent).toContain('não tem referência ao código');
    expect(el.querySelector('.biz-open')).toBeNull();
  });

  // WEB-BIZV-010
  it('painel_mostraOCaminhoDaEntradaComORotuloDoRamoEClicarNele_escolheOPasso', async () => {
    const { fixture, el, host, node } = await setup();

    node('s3').click();
    fixture.detectChanges();
    const items = Array.from(el.querySelectorAll('.biz-path li'));

    expect(items.map((li) => li.querySelector('button')!.textContent!.trim())).toEqual(['Pedido recebido', 'O pagamento foi aprovado?', 'Cliente é avisado do estorno']);
    expect(items.map((li) => li.querySelector('.biz-via')?.textContent ?? null)).toEqual([null, null, 'Se não']);
    (items[0].querySelector('button') as HTMLButtonElement).click();
    expect(host.picked).toEqual(['n4', 'n1']);
  });

  // WEB-BIZV-011
  it('passosDeCodigoAgrupados_ficamRecolhidosEClicarNeles_escolheONo', async () => {
    const { fixture, el, host, node } = await setup();

    node('s1').click();
    fixture.detectChanges();
    const grouped = el.querySelector<HTMLDetailsElement>('.biz-grouped')!;

    expect(grouped.open).toBeFalse();
    expect(grouped.querySelector('summary')!.textContent).toContain('1 passo de código neste bloco');
    (grouped.querySelector('.biz-grouped-list button') as HTMLButtonElement).click();
    expect(host.picked).toEqual(['n1', 'n2']);
  });

  // WEB-BIZV-012
  it('notaDoPasso_apareceNoPainelQuandoExiste', async () => {
    const { fixture, el, node } = await setup();

    node('s2').click();
    fixture.detectChanges();
    expect(el.querySelector('.biz-note')!.textContent).toBe('Nota de teste do passo');

    node('s1').click();
    fixture.detectChanges();
    expect(el.querySelector('.biz-note')).toBeNull();
  });

  // WEB-BIZV-013
  it('verNoGrafoEVerSoEsteTrecho_emitemOsNosDoPasso', async () => {
    const { fixture, host, node, button } = await setup();

    node('s2').click();
    fixture.detectChanges();
    button('Ver no grafo').click();
    button('Ver só este trecho').click();

    expect(host.inGraph).toEqual(['n3']);
    expect(host.focused).toEqual(['n3']);
  });

  // WEB-BIZV-070
  it('painel_verNoGrafoEOUnicoPrimarioEVerSoEsteTrechoExplicaNoTitle', async () => {
    const { fixture, el, node } = await setup();

    node('s2').click();
    fixture.detectChanges();
    const foot = el.querySelector('.biz-panel-foot')!;
    const primaries = Array.from(el.querySelectorAll('.biz-panel .btn-p'));
    const only = foot.querySelectorAll('button')[1];

    expect(primaries.length).toBe(1);
    expect(primaries[0]).toBe(foot.querySelectorAll('button')[0]);
    expect(only.getAttribute('title')).toBeTruthy();
    expect(only.getAttribute('title')).not.toBe(foot.querySelectorAll('button')[0].getAttribute('title'));
  });

  // WEB-BIZV-071
  it('seloProposto_levaADicaNoTitleENoTextoAcessivelEOTextoVisivelContinuaProposto', async () => {
    const { fixture, el, node } = await setup();

    const badge = node('s2').querySelector<HTMLElement>('.biz-badge.is-proposed')!;
    expect(badge.getAttribute('title')).toBeTruthy();
    expect(badge.querySelector('.visually-hidden')!.textContent).toContain(badge.getAttribute('title')!);
    expect(badge.firstChild!.textContent).toBe('proposto');

    node('s2').click();
    fixture.detectChanges();
    expect(el.querySelector('.biz-origin')!.getAttribute('title')).toBe(badge.getAttribute('title'));
  });

  // WEB-BIZV-072
  it('balao_aAlturaVemDoTextoEAVariavelDeLinhasAcompanha', async () => {
    const { node } = await setup();

    const box = node('s1');
    expect(box.style.getPropertyValue('--biz-lines')).toMatch(/^[1-6]$/);
  });

  // WEB-BIZV-014
  it('selecaoDeFora_acendeOBalaoQueResumeOnoEAbreOPainel', async () => {
    const { fixture, el, host, node } = await setup();

    host.selected.set('n2');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(node('s1').classList).toContain('is-selected');
    expect(el.querySelector('.biz-panel-title')!.textContent).toBe('Pedido recebido');
    host.selected.set(null);
    fixture.detectChanges();
    expect(el.querySelector('.biz-panel-title')).toBeNull();
  });

  // WEB-BIZV-015
  it('fechar_limpaOPassoEEmiteNulo', async () => {
    const { fixture, el, host, node, button } = await setup();

    node('s1').click();
    fixture.detectChanges();
    button('Fechar').click();
    fixture.detectChanges();

    expect(host.picked).toEqual(['n1', null]);
    expect(el.querySelector('.biz-panel-title')).toBeNull();
  });

  // WEB-BIZV-016
  it('semPassoAlemDaEntrada_explicaEOferecePararOGrafo', async () => {
    const { fixture, el, host, button } = await setup({ ...FLOW, steps: [FLOW.steps[0]], edges: [] });

    expect(el.textContent).toContain('Nenhuma decisão ou notificação encontrada neste fluxo.');
    expect(el.querySelector('f-flow')).toBeNull();
    button('Ver o grafo completo').click();
    fixture.detectChanges();

    expect(host.directions).toEqual([LayoutDirection.Horizontal]);
  });

  // WEB-BIZV-017
  it('decisoesAgrupadas_mostraOAvisoComAsContagens', async () => {
    const { el } = await setup({ ...FLOW, collapsedDecisions: 3 });

    expect(el.querySelector('.biz-bar-note')!.textContent).toContain('Fluxo resumido em 4 passos. 3 decisões foram agrupadas.');
  });

  // WEB-BIZV-018
  it('semDecisoesAgrupadas_naoMostraOAviso', async () => {
    const { el } = await setup();

    expect(el.querySelector('.biz-bar-note')).toBeNull();
  });

  // WEB-BIZV-019
  it('erroDaApi_mostraAMensagemPeloCodigoETentaDeNovo', async () => {
    const { fixture, el, http } = await setup({ status: 404 });

    expect(el.querySelector('[role="alert"]')!.textContent).toContain('Não encontrado.');
    expect(el.querySelector('.biz-node')).toBeNull();
    (el.querySelector('[role="alert"] button') as HTMLButtonElement).click();
    fixture.detectChanges();

    http.expectOne('/api/surveys/s1/business').flush(FLOW);
    http.expectOne('/api/surveys/s1/links').flush(LINKS);
    http.expectOne(LAYOUT_URL).flush(NO_LAYOUT);
    fixture.detectChanges();
    expect(el.querySelectorAll('.biz-node').length).toBe(4);
  });

  // WEB-BIZV-020
  it('carregando_mostraOAvisoAntesDaResposta', () => {
    TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Montando a visão de negócio');
    TestBed.inject(HttpTestingController).match(() => true).forEach((r) => r.flush(r.request.url.endsWith('/links') ? [] : r.request.url.endsWith('/layout') ? NO_LAYOUT : FLOW));
  });

  // WEB-BIZV-021
  it('botaoDoDesenho_voltaAoHorizontalEOResumoVaiParaOPainel', async () => {
    const { el, host } = await setup();

    (el.querySelector('[aria-label^="Desenho em visão de negócio"]') as HTMLButtonElement).click();

    expect(host.directions).toEqual([LayoutDirection.Horizontal]);
    expect(host.summaries.length).toBeGreaterThan(0);
  });

  // WEB-BIZV-022
  it('balao_eAlcancavelPeloTecladoEEnterEscolheOPasso', async () => {
    const { fixture, host, node } = await setup();

    expect(node('s1').getAttribute('tabindex')).toBe('0');
    expect(node('s1').getAttribute('role')).toBe('button');
    node('s1').dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();

    expect(host.picked).toEqual(['n1']);
  });

  // WEB-BIZV-023
  it('exportDrawio_geraOLosangoEOLinkDeCadaBalao', async () => {
    const { fixture } = await setup();
    const view = fixture.debugElement.children[0].componentInstance as BusinessViewComponent;

    const xml = view.exportDrawio('teste', (repository, location) => (location ? `https://host/${repository}/${location.file}#${location.line}` : null))!;

    expect(xml).toContain('rhombus');
    expect(xml).toContain('https://host/pedidos-web/src/Pedidos.cs#10');
    expect(xml).toContain('O pagamento foi aprovado?');
  });

  // WEB-BIZV-024
  it('botoesDeZoom_atualizamOPercentualEEnquadrar_naoQuebra', async () => {
    const { fixture, el, button } = await setup();

    button('+').click();
    await new Promise((resolve) => setTimeout(resolve, 80));
    fixture.detectChanges();
    const aumentado = el.querySelector('.biz-zoom-value')!.textContent!;
    button('⤢').click();
    await new Promise((resolve) => setTimeout(resolve, 80));
    fixture.detectChanges();

    expect(aumentado).toMatch(/^\d+%$/);
    expect(el.querySelector('.biz-zoom-value')!.textContent).toMatch(/^\d+%$/);
  });

  // ---- Fase 58: tela livre e posição salva ----

  const positionOf = (fixture: { debugElement: DebugElement }, id: string) =>
    fixture.debugElement
      .queryAll(By.directive(FNodeDirective))
      .find((d) => (d.nativeElement as HTMLElement).dataset['stepId'] === id)!
      .injector.get(FNodeDirective)
      .position();
  const nodeDebug = (fixture: { debugElement: DebugElement }, id: string) => fixture.debugElement.queryAll(By.directive(FNodeDirective)).find((d) => (d.nativeElement as HTMLElement).dataset['stepId'] === id)!;
  const move = (fixture: { debugElement: DebugElement }, id: string, point: { x: number; y: number }) => nodeDebug(fixture, id).triggerEventHandler('fNodePositionChange', point);

  // WEB-BIZV-034
  it('posicaoSalva_venceAAutomaticaEPassoSemPosicaoFicaNaAutomatica', async () => {
    const automatic = businessLayout(FLOW).boxes;
    const { fixture } = await setup(FLOW, LINKS, { steps: [{ stepId: 's3', x: 500, y: 300 }], edges: [] });

    expect(positionOf(fixture, 's3')).toEqual({ x: 500, y: 300 });
    expect(positionOf(fixture, 's1')).toEqual({ x: automatic.get('s1')!.x, y: automatic.get('s1')!.y });
  });

  // WEB-BIZV-035
  it('arrastarOBalao_encaixaNaGradeEGravaDepoisDoDebounceEmLote', async () => {
    const { fixture, http } = await setup();

    move(fixture, 's2', { x: 123, y: 57 });
    move(fixture, 's3', { x: 201, y: 98 });
    fixture.detectChanges();
    http.expectNone(LAYOUT_URL);
    await afterDebounce();

    const put = http.expectOne((r) => r.method === 'PUT' && r.url === LAYOUT_URL);
    expect(put.request.body.steps).toEqual([
      { stepId: 's2', x: 120, y: 60 },
      { stepId: 's3', x: 200, y: 100 },
    ]);
    expect(positionOf(fixture, 's2')).toEqual({ x: 120, y: 60 });
    put.flush(null);
  });

  // WEB-BIZV-036
  it('movimentoNaMesmaPosicao_naoGrava', async () => {
    const automatic = businessLayout(FLOW).boxes.get('s1')!;
    const { fixture, http } = await setup();

    move(fixture, 's1', { x: automatic.x + 2, y: automatic.y - 3 });
    await afterDebounce();

    http.expectNone((r) => r.method === 'PUT');
  });

  // WEB-BIZV-037
  it('falhaAoSalvar_avisaSemDesfazerOMovimentoEATentativaSeguinteLevaOQueFicou', async () => {
    const { fixture, el, http } = await setup();

    move(fixture, 's2', { x: 300, y: 200 });
    await afterDebounce();
    http.expectOne((r) => r.method === 'PUT').flush({ code: 'ERRO' }, { status: 500, statusText: 'Erro' });
    fixture.detectChanges();

    expect(el.querySelector('.toast')!.textContent).toContain('Não deu para salvar a posição');
    expect(positionOf(fixture, 's2')).toEqual({ x: 300, y: 200 });
    move(fixture, 's3', { x: 400, y: 200 });
    await afterDebounce();
    const retry = http.expectOne((r) => r.method === 'PUT');
    expect(retry.request.body.steps.map((x: { stepId: string }) => x.stepId)).toEqual(['s2', 's3']);
    retry.flush(null);
    fixture.detectChanges();
    expect(el.querySelector('.toast')).toBeNull();
  });

  // WEB-BIZV-038
  it('pontosDeDobra_viramPontosDaLigacaoESaemNoMesmoLote', async () => {
    const { fixture, http } = await setup();

    fixture.debugElement.query(By.css('f-flow')).triggerEventHandler('fConnectionWaypointsChanged', { connectionId: 'e1', waypoints: [{ x: 203, y: 98 }] });
    fixture.detectChanges();
    await afterDebounce();

    const put = http.expectOne((r) => r.method === 'PUT');
    expect(put.request.body.edges).toEqual([{ from: 's2', to: 's3', points: [{ x: 200, y: 100 }] }]);
    const waypoints = fixture.debugElement.queryAll(By.directive(FConnectionWaypoints)).map((d) => d.injector.get(FConnectionWaypoints).waypoints());
    expect(waypoints[1]).toEqual([{ x: 200, y: 100 }]);
    expect(waypoints[0]).toEqual([]);
    put.flush(null);
  });

  // WEB-BIZV-039
  it('pontosDeDobraSalvos_voltamNaLigacaoCerta', async () => {
    const { fixture } = await setup(FLOW, LINKS, { steps: [], edges: [{ from: 's2', to: 's4', points: [{ x: 30, y: 40 }, { x: 50, y: 60 }] }] });

    const waypoints = fixture.debugElement.queryAll(By.directive(FConnectionWaypoints)).map((d) => d.injector.get(FConnectionWaypoints).waypoints());

    expect(waypoints[2]).toEqual([{ x: 30, y: 40 }, { x: 50, y: 60 }]);
  });

  // WEB-BIZV-040
  it('falhaAoCarregarAPosicao_desenhaNoAutomaticoEAvisa', async () => {
    const automatic = businessLayout(FLOW).boxes.get('s2')!;
    const { fixture, el } = await setup(FLOW, LINKS, { status: 500 });

    expect(el.querySelectorAll('.biz-node').length).toBe(4);
    expect(positionOf(fixture, 's2')).toEqual({ x: automatic.x, y: automatic.y });
    expect(el.querySelector('.toast')!.textContent).toContain('Não deu para carregar as posições salvas');
  });

  // WEB-BIZV-041
  it('reorganizar_soApareceComPosicaoSalvaPedeConfirmacaoEVoltaAoAutomatico', async () => {
    const automatic = businessLayout(FLOW).boxes.get('s3')!;
    const { fixture, el, button, http } = await setup(FLOW, LINKS, { steps: [{ stepId: 's3', x: 500, y: 300 }], edges: [] });

    expect(button('Reorganizar')).toBeDefined();
    button('Reorganizar').click();
    fixture.detectChanges();
    expect(el.querySelector('app-confirm-dialog')).not.toBeNull();
    http.expectNone((r) => r.method === 'DELETE');
    (Array.from(el.querySelectorAll<HTMLButtonElement>('app-confirm-dialog button')).find((b) => b.textContent!.includes('Reorganizar')))!.click();
    http.expectOne((r) => r.method === 'DELETE' && r.url === LAYOUT_URL).flush(null);
    fixture.detectChanges();

    expect(positionOf(fixture, 's3')).toEqual({ x: automatic.x, y: automatic.y });
    expect(Array.from(el.querySelectorAll('button')).some((b) => b.textContent!.trim() === 'Reorganizar')).toBeFalse();
  });

  // WEB-BIZV-042
  it('reorganizarCancelado_naoApagaNada', async () => {
    const { fixture, el, button, http } = await setup(FLOW, LINKS, { steps: [{ stepId: 's3', x: 500, y: 300 }], edges: [] });

    button('Reorganizar').click();
    fixture.detectChanges();
    (Array.from(el.querySelectorAll<HTMLButtonElement>('app-confirm-dialog button')).find((b) => b.textContent!.includes('Cancelar')))!.click();
    fixture.detectChanges();

    http.expectNone((r) => r.method === 'DELETE');
    expect(el.querySelector('app-confirm-dialog')).toBeNull();
    expect(positionOf(fixture, 's3')).toEqual({ x: 500, y: 300 });
  });

  // WEB-BIZV-056
  it('semPosicaoSalva_naoHaBotaoReorganizar', async () => {
    const { el } = await setup();

    expect(Array.from(el.querySelectorAll('button')).some((b) => b.textContent!.trim() === 'Reorganizar')).toBeFalse();
  });

  // WEB-BIZV-043
  it('cliqueSemArrastar_abreOPainel_eArrastarNaoAbre', async () => {
    const { fixture, host, node } = await setup();
    const press = (x: number) => node('s2').dispatchEvent(new PointerEvent('pointerdown', { clientX: x, clientY: 10, bubbles: true }));
    const release = (x: number) => node('s2').dispatchEvent(new MouseEvent('click', { clientX: x, clientY: 10, bubbles: true }));

    press(10);
    release(10 + CLICK_TOLERANCE_PX + 20);
    fixture.detectChanges();
    expect(host.picked).toEqual([]);

    press(10);
    release(10 + CLICK_TOLERANCE_PX - 1);
    fixture.detectChanges();
    expect(host.picked).toEqual(['n3']);
  });

  // WEB-BIZV-044
  it('tab_andaNaOrdemDoFluxoEIgnoraOndeOFoblexPosOsNosNoDom', async () => {
    const { fixture, el, node } = await setup();
    // O Foblex leva o balão clicado para o fim do DOM: a ordem dos nós deixa de ser a do fluxo.
    const canvasNodes = node('s1').parentElement!;
    canvasNodes.appendChild(node('s1'));
    node('s2').focus();
    fixture.detectChanges();
    const tab = (id: string, shift = false) => {
      const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey: shift, bubbles: true, cancelable: true });
      node(id).dispatchEvent(event);
      fixture.detectChanges();
      return event.defaultPrevented;
    };

    expect(tab('s2')).toBeTrue();
    expect(document.activeElement).toBe(node('s3'));
    expect(node('s3').getAttribute('tabindex')).toBe('0');
    expect(node('s1').getAttribute('tabindex')).toBe('-1');
    expect(tab('s3', true)).toBeTrue();
    expect(document.activeElement).toBe(node('s2'));
    expect(tab('s2', true)).toBeTrue();
    expect(document.activeElement).toBe(node('s1'));
    // Nas pontas o Tab segue o caminho normal e sai do desenho.
    expect(tab('s1', true)).toBeFalse();
    node('s4').focus();
    expect(tab('s4')).toBeFalse();
    expect(el.querySelectorAll('[tabindex="0"].biz-node').length).toBe(1);
  });

  // WEB-BIZV-045
  it('balaoEscolhido_viraAParadaDeTab', async () => {
    const { fixture, node } = await setup();

    node('s3').click();
    fixture.detectChanges();

    expect(node('s3').getAttribute('tabindex')).toBe('0');
    expect(node('s1').getAttribute('tabindex')).toBe('-1');
  });

  // WEB-BIZV-046
  it('setas_movemOBalaoDezPixelsEGravam', async () => {
    const automatic = businessLayout(FLOW).boxes.get('s2')!;
    const { fixture, node, http } = await setup();

    node('s2').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }));
    node('s2').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
    fixture.detectChanges();

    expect(positionOf(fixture, 's2')).toEqual({ x: automatic.x + 10, y: automatic.y + 10 });
    await afterDebounce();
    const put = http.expectOne((r) => r.method === 'PUT');
    expect(put.request.body.steps).toEqual([{ stepId: 's2', x: automatic.x + 10, y: automatic.y + 10 }]);
    put.flush(null);
  });

  // WEB-BIZV-047
  it('esc_limpaAEscolhaEOPainelFecha', async () => {
    const { fixture, el, host, node } = await setup();

    node('s1').click();
    fixture.detectChanges();
    node('s1').dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    fixture.detectChanges();

    expect(host.picked).toEqual(['n1', null]);
    expect(el.querySelector('.biz-panel-title')).toBeNull();
  });

  // WEB-BIZV-048
  it('teclaZero_voltaOZoomA100PorCento_eMaisEMenosNaoQuebram', async () => {
    const { fixture, el, node } = await setup();
    const key = (k: string) => node('s1').dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));

    key('+');
    key('-');
    key('0');
    await wait(80);
    fixture.detectChanges();

    expect(el.querySelector('.biz-zoom-value')!.textContent).toBe('100%');
  });

  // WEB-BIZV-049
  it('botaoDe100PorCento_voltaAEscalaNormal', async () => {
    const { fixture, el } = await setup();

    el.querySelector<HTMLButtonElement>('.biz-zoom-value')!.click();
    await wait(80);
    fixture.detectChanges();

    expect(el.querySelector('.biz-zoom-value')!.textContent).toBe('100%');
    expect(el.querySelector('.biz-zoom-value')!.getAttribute('aria-label')).toContain('Voltar para 100%');
  });

  // WEB-BIZV-050
  it('zoomEntre25e140PorCento_comRodaSemTeclaRolandoEComCtrlAproximando', async () => {
    const { fixture } = await setup();

    const zoom = fixture.debugElement.query(By.directive(FZoomDirective)).injector.get(FZoomDirective);
    const scheme = fixture.debugElement.query(By.css('f-flow')).injector.get(FControlSchemeController).getScheme();

    expect(zoom.minimum).toBe(ZOOM_MIN);
    expect(zoom.maximum).toBe(ZOOM_MAX);
    expect(ZOOM_MIN).toBe(0.25);
    expect(ZOOM_MAX).toBe(1.4);
    expect(scheme.scrollPan).toBeTrue();
  });

  // WEB-BIZV-082 (passo de UX da release 0.11.0: em 360 px o enquadramento chegava a 2%)
  it('ajustar_naoDeixaOZoomAbaixoDoMinimo', async () => {
    const { fixture, el } = await setup();
    const host = fixture.debugElement.query(By.directive(FCanvasComponent));
    const canvas = host.componentInstance as FCanvasComponent;
    const setScale = spyOn(canvas, 'setScale').and.callThrough();

    // O enquadramento numa área pequena demais avisa 2% na mudança do canvas.
    host.triggerEventHandler('fCanvasChange', { scale: 0.02 });
    fixture.detectChanges();

    expect(setScale).toHaveBeenCalledWith(ZOOM_MIN);
    expect(el.querySelector('.biz-zoom-value')!.textContent).toBe('25%');
  });

  // WEB-BIZV-051
  it('grade_deDezPixelsEOBalaoTemAAlcaDeArrastar', async () => {
    const { fixture, node } = await setup();

    const draggable = fixture.debugElement.query(By.directive(FDraggableDirective)).injector.get(FDraggableDirective);

    expect(draggable.hCellSize()).toBe(10);
    expect(draggable.vCellSize()).toBe(10);
    expect(draggable.fCellSizeWhileDragging()).toBeTrue();
    expect(node('s1').classList).toContain('f-drag-handle');
  });

  // WEB-BIZV-052
  it('espacoSegurado_viraAMaoParaMoverATelaMesmoSobreOBalao', async () => {
    const { fixture, el, node } = await setup();
    const flow = el.querySelector('f-flow')!;
    const draggable = fixture.debugElement.query(By.directive(FDraggableDirective)).injector.get(FDraggableDirective);
    const primary = { buttons: 1 } as unknown as MouseEvent;

    expect(draggable.fNodeMoveTrigger(primary)).toBeTrue();
    node('s2').focus();
    node('s2').dispatchEvent(new KeyboardEvent('keydown', { key: ' ', code: 'Space', bubbles: true, cancelable: true }));
    fixture.detectChanges();

    expect(flow.classList).toContain('is-panning');
    expect(draggable.fNodeMoveTrigger(primary)).toBeFalse();
    expect(draggable.fCanvasMoveTrigger(primary)).toBeTrue();
    document.dispatchEvent(new KeyboardEvent('keyup', { key: ' ', code: 'Space', bubbles: true }));
    fixture.detectChanges();
    expect(flow.classList).not.toContain('is-panning');
  });

  // WEB-BIZV-053
  it('espacoNoBotaoDoPainel_naoViraAMao', async () => {
    const { fixture, el, node, button } = await setup();

    node('s1').click();
    fixture.detectChanges();
    button('Fechar').dispatchEvent(new KeyboardEvent('keydown', { key: ' ', code: 'Space', bubbles: true, cancelable: true }));
    fixture.detectChanges();

    expect(el.querySelector('f-flow')!.classList).not.toContain('is-panning');
  });

  // WEB-BIZV-054
  it('abrirOPainel_trazOBalaoParaAAreaVisivelSoQuandoEstaFora', async () => {
    const { fixture, el, node } = await setup();
    const canvas = fixture.debugElement.query(By.directive(FCanvasComponent)).componentInstance as FCanvasComponent;
    const center = spyOn(canvas, 'centerGroupOrNode');
    const rect = (left: number, top: number, width: number, height: number) => ({ left, top, right: left + width, bottom: top + height, width, height, x: left, y: top, toJSON: () => ({}) }) as DOMRect;
    spyOn(el.querySelector('.biz-stage')!, 'getBoundingClientRect').and.returnValue(rect(0, 0, 900, 300));
    const rects = spyOn(node('s2'), 'getBoundingClientRect');

    rects.and.returnValue(rect(100, 100, 200, 80));
    node('s2').click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(center).not.toHaveBeenCalled();

    rects.and.returnValue(rect(100, 260, 200, 80));
    node('s2').click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(center).toHaveBeenCalledWith('s2', true);
  });

  // WEB-BIZV-055
  it('desenho_apontaParaAsTeclasEOTextoDeAjudaExiste_eOsBaloesNaoRepetemAReferencia', async () => {
    const { el, node } = await setup();
    const stage = el.querySelector('.biz-stage')!;
    const helpId = stage.getAttribute('aria-describedby')!;

    expect(helpId).toMatch(/^biz-keys-help-\d+$/);
    expect(el.querySelector(`#${helpId}`)!.textContent).toContain('Tab');
    expect(node('s1').hasAttribute('aria-describedby')).toBeFalse();
  });

  // WEB-BIZV-057
  it('portasVaziasDoFoblex_ficamEscondidasDoLeitorDeTelaEANotaDoResumoEStatus', async () => {
    const { el } = await setup({ ...FLOW, collapsedDecisions: 2 });

    const ports = Array.from(el.querySelectorAll('.biz-port'));
    expect(ports.length).toBe(8);
    expect(ports.every((port) => port.getAttribute('aria-hidden') === 'true')).toBeTrue();
    expect(el.querySelector('.biz-bar-note')!.getAttribute('role')).toBe('status');
  });

  // WEB-BIZV-058
  // Só o hover da ligação os mostra (não dá para simular hover por CSS no teste); o foco num balão não os acende,
  // porque o clique num balão deixa o foco no desenho e os círculos voltavam a poluir tudo (visto no navegador).
  it('pontosDeDobraCandidatos_ficamInvisiveisMesmoComFocoNoBalao_osReaisFicam', async () => {
    const { fixture, el, node } = await setup();
    const host = el.querySelector('.biz-host')!;
    const connection = document.createElement('div');
    connection.className = 'f-connection';
    connection.innerHTML = '<span class="f-candidate"></span><span class="f-waypoint"></span>';
    host.appendChild(connection);
    const opacity = (selector: string) => getComputedStyle(connection.querySelector(selector)!).opacity;

    expect(opacity('.f-candidate')).toBe('0');
    expect(opacity('.f-waypoint')).toBe('1');
    node('s1').focus();
    fixture.detectChanges();
    await wait(250);
    expect(opacity('.f-candidate')).toBe('0');
    expect(opacity('.f-waypoint')).toBe('1');
    connection.remove();
  });

  // WEB-BIZV-059
  it('painel_limitaAAlturaEOConteudoTemMargemMinimaDeDezesseisPixels', async () => {
    const { fixture, el, node } = await setup();
    node('s1').click();
    fixture.detectChanges();

    const panel = el.querySelector('.biz-panel')!;
    const style = getComputedStyle(panel);

    expect(style.maxHeight).not.toBe('none');
    expect(style.paddingLeft).toBe('16px');
  });

  // WEB-BIZV-073
  it('balaoComTextoDeAgenteIaOuUsuario_mostraOSeloCurtoDaOrigemENoAriaLabel_regraNaoMostra', async () => {
    const flow = {
      ...FLOW,
      steps: FLOW.steps.map((s) => (s.id === 's1' ? { ...s, labelOrigin: LabelOrigin.User } : s.id === 's4' ? { ...s, labelOrigin: LabelOrigin.Agent } : s)),
    };
    const { node } = await setup(flow);

    expect(node('s1').querySelector('.biz-badge.is-origin')!.textContent).toBe('editado');
    expect(node('s1').getAttribute('aria-label')).toContain('texto por você');
    expect(node('s2').querySelector('.biz-badge.is-origin')!.textContent).toBe('IA');
    expect(node('s4').querySelector('.biz-badge.is-origin')!.textContent).toBe('agente');
    expect(node('s3').querySelector('.biz-badge.is-origin')).toBeNull();
    expect(BUSINESS_ORIGIN_BADGE_KEY[LabelOrigin.Rule]).toBeNull();
  });

  // WEB-BIZV-074
  it('losangoEscolhido_temOAnelDaEscolhaAtrasDoTracoPrincipal', async () => {
    const { fixture, node } = await setup();
    node('s2').click();
    fixture.detectChanges();

    const [ring, main] = Array.from(node('s2').querySelectorAll('.biz-shape polygon'));

    expect(ring.classList).toContain('biz-ring');
    expect(main.classList).not.toContain('biz-ring');
    expect(getComputedStyle(ring).stroke).not.toBe('none');
    expect(parseFloat(getComputedStyle(ring).strokeWidth)).toBeGreaterThan(parseFloat(getComputedStyle(main).strokeWidth));
  });
});

describe('BusinessViewComponent (Fase 59: texto de negócio)', () => {
  const STEP_URL = (id: string, tail: string) => `/api/surveys/s1/business/steps/${id}/${tail}`;
  const typeInto = (input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };
  const key = (target: HTMLElement, k: string) => target.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));
  const withAi = (fixture: { detectChanges(): void }, uses: AiUse[]) => {
    TestBed.inject(AiStatusService).status.set({ enabled: true, configured: true, provider: AiProvider.Claude, model: 'm', uses, tokensToday: 0, dailyTokenBudget: 0 });
    fixture.detectChanges();
  };
  const panelInput = (el: HTMLElement) => el.querySelector<HTMLInputElement>('.biz-panel input[data-edit]')!;
  const nodeMove = (fixture: { debugElement: DebugElement }, id: string, point: { x: number; y: number }) =>
    fixture.debugElement.queryAll(By.directive(FNodeDirective)).find((d) => (d.nativeElement as HTMLElement).dataset['stepId'] === id)!.triggerEventHandler('fNodePositionChange', point);

  // WEB-BIZV-060
  it('editarNoPainel_enterSalvaOTextoEAtualizaOBalaoEAOrigem', async () => {
    const { fixture, el, node, button, http } = await setup();
    node('s1').click();
    fixture.detectChanges();

    button('Editar texto').click();
    fixture.detectChanges();
    await fixture.whenStable();
    const input = panelInput(el);
    expect(input.maxLength).toBe(120);
    typeInto(input, '  Pedido   chegou ao sistema  ');
    key(input, 'Enter');
    fixture.detectChanges();

    const put = http.expectOne(STEP_URL('s1', 'label'));
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ text: 'Pedido chegou ao sistema' });
    put.flush(step('s1', 'n1', BusinessKind.Start, 'Pedido chegou ao sistema', { labelOrigin: LabelOrigin.User, labelState: LabelState.Confirmed, sourceNodeIds: ['n1', 'n2'] }));
    fixture.detectChanges();

    expect(node('s1').querySelector('.biz-text')!.textContent).toContain('Pedido chegou ao sistema');
    expect(el.querySelector('.biz-origin')!.textContent).toContain('Confirmado por você');
    expect(el.querySelector('.toast')!.textContent).toContain('Texto salvo');
  });

  // WEB-BIZV-061
  it('editarComEsc_cancelaESemMudancaNaoGrava', async () => {
    const { fixture, el, node, button, http } = await setup();
    node('s1').click();
    fixture.detectChanges();
    button('Editar texto').click();
    fixture.detectChanges();
    await fixture.whenStable();

    typeInto(panelInput(el), 'outro texto');
    key(panelInput(el), 'Escape');
    fixture.detectChanges();
    expect(el.querySelector('.biz-panel input[data-edit]')).toBeNull();

    button('Editar texto').click();
    fixture.detectChanges();
    key(panelInput(el), 'Enter');
    fixture.detectChanges();

    http.expectNone((r) => r.method === 'PUT');
    expect(el.querySelector('.biz-panel-title')!.textContent).toContain('Pedido recebido');
  });

  // WEB-BIZV-062
  it('duploCliqueNoBalao_editaNoProprioBalaoSemAbrirOPainelNemMoverOBalao', async () => {
    const { fixture, el, node, http } = await setup();

    node('s4').dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
    fixture.detectChanges();
    await fixture.whenStable();
    const input = node('s4').querySelector<HTMLInputElement>('input[data-edit]')!;
    expect(input).not.toBeNull();
    expect(node('s4').querySelector('.biz-text')).toBeNull();
    expect(input.value).toBe('Pedido concluído');

    // As teclas do campo não sobem para o balão: seta não move.
    const arrow = new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true });
    input.dispatchEvent(arrow);
    expect(arrow.defaultPrevented).toBeFalse();

    typeInto(input, 'Pedido entregue');
    key(input, 'Enter');
    fixture.detectChanges();
    const put = http.expectOne(STEP_URL('s4', 'label'));
    expect(put.request.body).toEqual({ text: 'Pedido entregue' });
    put.flush(step('s4', 'n5', BusinessKind.End, 'Pedido entregue', { labelOrigin: LabelOrigin.User }));
    fixture.detectChanges();

    expect(node('s4').querySelector('.biz-text')!.textContent).toContain('Pedido entregue');
    expect(el.querySelector('.biz-panel-title')).toBeNull();
  });

  // WEB-BIZV-063
  it('textoVazio_enviaNuloEVoltaAoTextoDaRegra', async () => {
    const { fixture, el, node, button, http } = await setup();
    node('s1').click();
    fixture.detectChanges();
    button('Editar texto').click();
    fixture.detectChanges();
    await fixture.whenStable();

    typeInto(panelInput(el), '   ');
    key(panelInput(el), 'Enter');
    fixture.detectChanges();

    expect(http.expectOne(STEP_URL('s1', 'label')).request.body).toEqual({ text: null });
  });

  // WEB-BIZV-064
  it('textoDoUsuario_oferecePorUsarOTextoDaRegra', async () => {
    const user = { ...FLOW, steps: FLOW.steps.map((s) => (s.id === 's1' ? { ...s, labelOrigin: LabelOrigin.User } : s)) };
    const { fixture, node, button, http } = await setup(user);
    node('s1').click();
    fixture.detectChanges();

    button('Usar o texto da regra').click();
    fixture.detectChanges();

    const put = http.expectOne(STEP_URL('s1', 'label'));
    expect(put.request.body).toEqual({ text: null });
    put.flush(step('s1', 'n1', BusinessKind.Start, 'Pedido recebido', { sourceNodeIds: ['n1', 'n2'] }));
  });

  // WEB-BIZV-065
  it('textoProposto_ficaEmItalicoComSeloEConfirmarGravaEOSeloSai', async () => {
    const { fixture, el, node, button, http } = await setup();
    const hasConfirm = () => Array.from(el.querySelectorAll('.biz-panel button')).some((b) => b.textContent!.trim() === 'Confirmar');

    expect(node('s2').classList).toContain('is-proposed');
    expect(getComputedStyle(node('s2').querySelector('.biz-text')!).fontStyle).toBe('italic');
    expect(getComputedStyle(node('s1').querySelector('.biz-text')!).fontStyle).toBe('normal');

    node('s1').click();
    fixture.detectChanges();
    expect(hasConfirm()).toBeFalse();
    node('s2').click();
    fixture.detectChanges();
    expect(el.querySelector('.biz-origin')!.textContent).toContain('Proposto pela IA');
    expect(hasConfirm()).toBeTrue();

    button('Confirmar').click();
    const post = http.expectOne(STEP_URL('s2', 'label/confirm'));
    expect(post.request.method).toBe('POST');
    post.flush(step('s2', 'n3', BusinessKind.Decision, 'O pagamento foi aprovado?', { labelOrigin: LabelOrigin.Ai, labelState: LabelState.Confirmed }));
    fixture.detectChanges();

    expect(node('s2').querySelector('.biz-badge.is-proposed')).toBeNull();
    expect(el.querySelector('.biz-origin')!.textContent).toContain('Confirmado pela IA');
  });

  // WEB-BIZV-066
  it('codigoMudou_mostraOSeloNoPainelSoNoPassoQueMudou', async () => {
    const changed = { ...FLOW, steps: FLOW.steps.map((s) => (s.id === 's4' ? { ...s, codeChanged: true } : s)) };
    const { fixture, el, node } = await setup(changed);

    node('s1').click();
    fixture.detectChanges();
    expect(el.querySelector('.biz-badge.is-changed')).toBeNull();
    node('s4').click();
    fixture.detectChanges();

    expect(el.querySelector('.biz-badge.is-changed')!.textContent).toContain('Código mudou desde a confirmação');
  });

  // WEB-BIZV-067
  it('trocarOTipo_enviaOTipoEDetectadoEnviaNuloEODesenhoUsaOFluxoDaResposta', async () => {
    const { fixture, el, node, http } = await setup();
    node('s4').click();
    fixture.detectChanges();
    const select = () => el.querySelector<HTMLSelectElement>('select[id^="biz-kind-pick"]')!;
    const options = Array.from(select().options).map((o) => o.textContent!.trim());
    expect(options[0]).toBe('Detectado');
    expect(select().options[0].value).toBe('-1');
    expect(options.length).toBe(8);
    expect(select().value).toBe(String(BusinessKind.End));

    select().value = String(BusinessKind.Rejection);
    select().dispatchEvent(new Event('change'));
    const put = http.expectOne(STEP_URL('s4', 'kind'));
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ kind: BusinessKind.Rejection });
    put.flush({ ...FLOW, steps: FLOW.steps.map((s) => (s.id === 's4' ? { ...s, kind: BusinessKind.Rejection } : s)) });
    fixture.detectChanges();
    expect(node('s4').classList).toContain('biz-kind-rejection');

    select().value = String(KIND_DETECTED);
    select().dispatchEvent(new Event('change'));
    const back = http.expectOne(STEP_URL('s4', 'kind'));
    expect(back.request.body).toEqual({ kind: null });
    back.flush(FLOW);
    fixture.detectChanges();
    expect(node('s4').classList).toContain('biz-kind-end');
  });

  // WEB-BIZV-068
  it('propostaDeTipoDoAgente_mostraOMotivoEConfirmarEDescartarChamamAsRotas', async () => {
    const proposal = { kind: BusinessKind.Notification, reason: 'Envia e-mail ao cliente' };
    const withProposal = { ...FLOW, steps: FLOW.steps.map((s) => (s.id === 's4' ? { ...s, kindProposal: proposal } : s)) };
    const { fixture, el, node, http } = await setup(withProposal);
    const proposalButton = (text: string) => Array.from(el.querySelectorAll<HTMLButtonElement>('.biz-proposal button')).find((b) => b.textContent!.trim() === text)!;
    node('s1').click();
    fixture.detectChanges();
    expect(el.querySelector('.biz-proposal')).toBeNull();

    node('s4').click();
    fixture.detectChanges();
    expect(el.querySelector('.biz-proposal')!.textContent).toContain('O agente propõe o tipo Notificação');
    expect(el.querySelector('.biz-proposal')!.textContent).toContain('Envia e-mail ao cliente');

    proposalButton('Confirmar').click();
    const confirm = http.expectOne(STEP_URL('s4', 'kind/confirm'));
    expect(confirm.request.method).toBe('POST');
    confirm.flush({ ...FLOW, steps: FLOW.steps.map((s) => (s.id === 's4' ? { ...s, kind: BusinessKind.Notification } : s)) });
    fixture.detectChanges();
    expect(el.querySelector('.biz-proposal')).toBeNull();
    expect(node('s4').classList).toContain('biz-kind-notification');

    // Descartar: a proposta de outro passo, no mesmo fluxo.
    const again = { ...FLOW, steps: FLOW.steps.map((s) => (s.id === 's4' ? { ...s, kindProposal: proposal } : s)) };
    (fixture.debugElement.query(By.directive(BusinessViewComponent)).componentInstance as { flow: { set(f: BusinessFlow): void } }).flow.set(again);
    fixture.detectChanges();
    proposalButton('Descartar').click();
    const dismiss = http.expectOne(STEP_URL('s4', 'kind/dismiss'));
    expect(dismiss.request.method).toBe('POST');
    dismiss.flush(FLOW);
    fixture.detectChanges();
    expect(el.querySelector('.biz-proposal')).toBeNull();
  });

  // WEB-BIZV-069
  it('sugerirTextos_soApareceComIaLigadaEOUsoSugestoes_nuncaBotaoMorto', async () => {
    const { fixture, el } = await setup();
    const suggest = () => Array.from(el.querySelectorAll('.biz-bar button')).find((b) => b.textContent!.includes('Sugerir textos'));

    expect(suggest()).toBeUndefined();
    withAi(fixture, [AiUse.FillHoles]);
    expect(suggest()).toBeUndefined();
    TestBed.inject(AiStatusService).status.update((s) => ({ ...s!, configured: false, uses: [AiUse.Suggestions] }));
    fixture.detectChanges();
    expect(suggest()).toBeUndefined();
    withAi(fixture, [AiUse.Suggestions]);
    expect(suggest()).toBeDefined();
  });

  // WEB-BIZV-070
  it('sugerirTextos_mostraCarregandoDizQuantosForamPropostosERecarregaAVisao', async () => {
    const { fixture, el, node, http } = await setup();
    withAi(fixture, [AiUse.Suggestions]);
    const bar = () => Array.from(el.querySelectorAll<HTMLButtonElement>('.biz-bar button')).find((b) => b.textContent!.includes('Sugerir') || b.textContent!.includes('Sugerindo'))!;

    bar().click();
    fixture.detectChanges();
    expect(bar().disabled).toBeTrue();
    expect(bar().textContent).toContain('Sugerindo');
    const post = http.expectOne('/api/surveys/s1/business/labels/suggest');
    expect(post.request.method).toBe('POST');
    post.flush({ proposed: 2, skipped: 1 });
    fixture.detectChanges();

    // O aviso só sai depois de o fluxo recarregado entrar no desenho.
    expect(bar().disabled).toBeFalse();
    expect(el.querySelector('.toast')).toBeNull();
    http.expectOne('/api/surveys/s1/business').flush({ ...FLOW, steps: FLOW.steps.map((s) => (s.id === 's4' ? { ...s, label: 'Pedido finalizado', labelOrigin: LabelOrigin.Ai, labelState: LabelState.Proposed } : s)) });
    fixture.detectChanges();
    expect(node('s4').querySelector('.biz-text')!.textContent).toContain('Pedido finalizado');
    expect(node('s4').querySelector('.biz-badge.is-proposed')).not.toBeNull();
    expect(el.querySelector('.toast')!.textContent).toContain('2 textos propostos');
  });

  // WEB-BIZV-071
  it('sugerirTextosComIaDesligadaNoServidor_mostraOErroPeloCodigo', async () => {
    const { fixture, el, http } = await setup();
    withAi(fixture, [AiUse.Suggestions]);

    Array.from(el.querySelectorAll<HTMLButtonElement>('.biz-bar button')).find((b) => b.textContent!.includes('Sugerir textos'))!.click();
    http.expectOne('/api/surveys/s1/business/labels/suggest').flush({ code: 'AI_DISABLED' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(el.querySelector('.toast')!.getAttribute('role')).toBe('alert');
    expect(el.querySelector('.toast')!.textContent).toContain('A IA está desligada');
  });

  // WEB-BIZV-072
  it('textoInvalido_mostraAMensagemDoCodigoENaoMudaOBalao', async () => {
    const { fixture, el, node, button, http } = await setup();
    node('s1').click();
    fixture.detectChanges();
    button('Editar texto').click();
    fixture.detectChanges();
    await fixture.whenStable();
    typeInto(panelInput(el), 'texto novo');
    key(panelInput(el), 'Enter');

    http.expectOne(STEP_URL('s1', 'label')).flush({ code: 'BUSINESS_LABEL_INVALID' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(el.querySelector('.toast')!.textContent).toContain('O texto do passo está vazio demais');
    expect(node('s1').querySelector('.biz-text')!.textContent).toContain('Pedido recebido');
  });

  // WEB-BIZV-075
  it('textoSalvo_oferecePorDesfazer_eDesfazerVoltaAoTextoDaRegraPelaMesmaRota', async () => {
    const { fixture, el, node, button, http } = await setup();
    node('s1').click();
    fixture.detectChanges();
    button('Editar texto').click();
    fixture.detectChanges();
    await fixture.whenStable();
    typeInto(panelInput(el), 'Pedido chegou');
    key(panelInput(el), 'Enter');
    http.expectOne(STEP_URL('s1', 'label')).flush(step('s1', 'n1', BusinessKind.Start, 'Pedido chegou', { labelOrigin: LabelOrigin.User, sourceNodeIds: ['n1', 'n2'] }));
    fixture.detectChanges();

    expect(el.querySelectorAll('.toast').length).toBe(1);
    expect(el.querySelector('.toast')!.textContent).toContain('Texto salvo');
    Array.from(el.querySelectorAll<HTMLButtonElement>('.toast button')).find((b) => b.textContent!.includes('Desfazer'))!.click();
    const put = http.expectOne(STEP_URL('s1', 'label'));
    expect(put.request.body).toEqual({ text: null });
    put.flush(step('s1', 'n1', BusinessKind.Start, 'Pedido recebido', { sourceNodeIds: ['n1', 'n2'] }));
    fixture.detectChanges();

    expect(node('s1').querySelector('.biz-text')!.textContent).toContain('Pedido recebido');
    expect(el.querySelector('.toast')!.textContent).toContain('Texto restaurado');
    expect(Array.from(el.querySelectorAll('.toast button')).some((b) => b.textContent!.includes('Desfazer'))).toBeFalse();
  });

  // WEB-BIZV-076
  it('desfazerDeTextoDoUsuario_enviaOTextoAnterior_eOAvisoSomeSozinhoDepoisDoTempo', async () => {
    const user = { ...FLOW, steps: FLOW.steps.map((s) => (s.id === 's1' ? { ...s, label: 'Meu texto', labelOrigin: LabelOrigin.User } : s)) };
    const { fixture, el, node, button, http } = await setup(user);
    node('s1').click();
    fixture.detectChanges();

    button('Usar o texto da regra').click();
    http.expectOne(STEP_URL('s1', 'label')).flush(step('s1', 'n1', BusinessKind.Start, 'Pedido recebido', { sourceNodeIds: ['n1', 'n2'] }));
    fixture.detectChanges();
    Array.from(el.querySelectorAll<HTMLButtonElement>('.toast button')).find((b) => b.textContent!.includes('Desfazer'))!.click();

    expect(http.expectOne(STEP_URL('s1', 'label')).request.body).toEqual({ text: 'Meu texto' });
    await wait(FEEDBACK_MS + 100);
    fixture.detectChanges();
    expect(el.querySelector('.toast')).toBeNull();
  });

  // WEB-BIZV-080
  it('textoPropostoPeloAgente_editado_naoOfereceDesfazer_paraNaoConfirmarAPropostaSemOUsuario', async () => {
    const proposed = {
      ...FLOW,
      steps: FLOW.steps.map((s) => (s.id === 's1' ? { ...s, label: 'Proposta do agente', labelOrigin: LabelOrigin.Agent, labelState: LabelState.Proposed } : s)),
    };
    const { fixture, el, node, button, http } = await setup(proposed);
    node('s1').click();
    fixture.detectChanges();
    button('Editar texto').click();
    fixture.detectChanges();
    await fixture.whenStable();
    typeInto(panelInput(el), 'Pedido chegou');
    key(panelInput(el), 'Enter');
    http.expectOne(STEP_URL('s1', 'label')).flush(step('s1', 'n1', BusinessKind.Start, 'Pedido chegou', { labelOrigin: LabelOrigin.User, sourceNodeIds: ['n1', 'n2'] }));
    fixture.detectChanges();

    expect(el.querySelector('.toast')!.textContent).toContain('Texto salvo');
    expect(Array.from(el.querySelectorAll('.toast button')).some((b) => b.textContent!.includes('Desfazer'))).toBeFalse();
  });

  // WEB-BIZV-077
  it('abrirNoHost_avisaQueEstaAbrindo_eAJanelaQueNaoAbriuViraAvisoDeErro', async () => {
    const { fixture, el, node, button } = await setup();
    node('s1').click();
    fixture.detectChanges();

    // Só as promessas da ponte (o whenStable esperaria o aviso sumir sozinho).
    const settle = async () => {
      await Promise.resolve();
      await Promise.resolve();
      fixture.detectChanges();
    };
    button('Abrir no GitHub').click();
    fixture.detectChanges();
    await settle();
    expect(el.querySelector('.toast')!.textContent).toContain('Abrindo');

    // A janela recusou o link (ou o sistema não abriu): a resposta da ponte é "não abriu".
    const bridge = TestBed.inject(DesktopBridge);
    (bridge as unknown as { openExternal: () => Promise<boolean> }).openExternal = () => Promise.resolve(false);
    button('Abrir no editor').click();
    fixture.detectChanges();
    await settle();

    expect(el.querySelector('.toast')!.getAttribute('role')).toBe('alert');
    expect(el.querySelector('.toast')!.textContent).toContain('Não foi possível abrir');
    expect(el.querySelectorAll('.toast').length).toBe(1);
  });

  // WEB-BIZV-078
  it('posicaoGravada_mostraPosicaoSalvaDiscretoEEleSome_semTomarOLugarDoAviso', async () => {
    const { fixture, el, http } = await setup();
    const saved = () => el.querySelector('.biz-saved')!;

    expect(saved().getAttribute('role')).toBe('status');
    expect(saved().textContent).toBe('');
    nodeMove(fixture, 's2', { x: 300, y: 200 });
    await afterDebounce();
    http.expectOne((r) => r.method === 'PUT').flush(null);
    fixture.detectChanges();

    expect(saved().textContent).toBe('Posição salva');
    expect(el.querySelector('.toast')).toBeNull();
    await wait(SAVED_MS + 100);
    fixture.detectChanges();
    expect(saved().textContent).toBe('');
  });

  // WEB-BIZV-079
  it('avisoDeFalhaESugestao_ocupamUmLugarSo', async () => {
    const { fixture, el, http } = await setup(FLOW, LINKS, { status: 500 });
    withAi(fixture, [AiUse.Suggestions]);

    expect(el.querySelectorAll('.toast').length).toBe(1);
    Array.from(el.querySelectorAll<HTMLButtonElement>('.biz-bar button')).find((b) => b.textContent!.includes('Sugerir textos'))!.click();
    http.expectOne('/api/surveys/s1/business/labels/suggest').flush({ code: 'AI_DISABLED' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(el.querySelectorAll('.toast').length).toBe(1);
    expect(el.querySelector('.toast')!.textContent).toContain('A IA está desligada');
  });
});

@Component({
  imports: [BusinessViewComponent],
  styles: ['app-business-view { display: block; width: 900px; height: 600px; }'],
  template: `<app-business-view [detail]="detail" [boardId]="boardId" [boardFlow]="flow()" [boardLayout]="layout()" />`,
})
class BoardHostComponent {
  readonly detail = detail;
  readonly boardId = 'b1';
  readonly flow = signal<BusinessFlow | null>(FLOW);
  readonly layout = signal<BusinessLayout | null>({ steps: [{ stepId: 's3', x: 500, y: 300 }], edges: [] });
}

describe('BusinessViewComponent (Fase 60, faixa de um quadro)', () => {
  const BOARD_LAYOUT_URL = '/api/boards/b1/layout';
  const positionOf = (fixture: { debugElement: DebugElement }, id: string) =>
    nodeDebug(fixture, id).injector.get(FNodeDirective).position();
  const nodeDebug = (fixture: { debugElement: DebugElement }, id: string) =>
    fixture.debugElement.queryAll(By.directive(FNodeDirective)).find((d) => (d.nativeElement as HTMLElement).dataset['stepId'] === id)!;

  async function boardSetup() {
    TestBed.configureTestingModule({
      imports: [BoardHostComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DesktopBridge, useValue: { openExternal: () => Promise.resolve(true) } }],
    });
    const fixture = TestBed.createComponent(BoardHostComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    // Na faixa do quadro o fluxo e a posição vêm do quadro: só os links se buscam.
    http.expectNone('/api/surveys/s1/business');
    http.expectNone('/api/surveys/s1/business/layout');
    http.expectOne('/api/surveys/s1/links').flush(LINKS);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el, host: fixture.componentInstance, http, node: (id: string) => el.querySelector<HTMLElement>(`[data-step-id="${id}"]`)! };
  }

  // WEB-BOARD-010
  it('faixa_desenhaOFluxoEAPosicaoQueVieramDoQuadro', async () => {
    const automatic = businessLayout(FLOW).boxes.get('s1')!;
    const { fixture, node } = await boardSetup();

    expect(node('s1')).not.toBeNull();
    expect(node('s4')).not.toBeNull();
    expect(positionOf(fixture, 's3')).toEqual({ x: 500, y: 300 });
    expect(positionOf(fixture, 's1')).toEqual({ x: automatic.x, y: automatic.y });
  });

  // WEB-BOARD-011
  it('faixa_naoMostraDirecaoSugerirTextosNemAcoesDoGrafo', async () => {
    const { fixture, el, node } = await boardSetup();
    TestBed.inject(AiStatusService).status.set({ enabled: true, configured: true, provider: AiProvider.Claude, model: 'm', uses: [AiUse.Suggestions], tokensToday: 0, dailyTokenBudget: 0 });
    fixture.detectChanges();

    const labels = Array.from(el.querySelectorAll('.biz-bar button')).map((b) => b.textContent!.trim());
    expect(labels.some((t) => t.includes('Sugerir textos'))).toBeFalse();
    expect(el.querySelector('.biz-bar [aria-label^="Desenho"]')).toBeNull();
    node('s2').click();
    fixture.detectChanges();
    expect(el.querySelector('.biz-panel')).not.toBeNull();
    expect(el.querySelector('.biz-panel-foot')).toBeNull();
  });

  // WEB-BOARD-012
  it('faixa_semPassoEscolhido_naoMostraOPainelNemADica', async () => {
    const { el } = await boardSetup();

    expect(el.querySelector('.biz-panel')).toBeNull();
  });

  // WEB-BOARD-013
  it('faixa_arrastarOBalaoGravaNoEscopoDoQuadroComOLevantamentoDaFaixa', async () => {
    const { fixture, http } = await boardSetup();

    nodeDebug(fixture, 's2').triggerEventHandler('fNodePositionChange', { x: 123, y: 57 });
    await afterDebounce();

    const put = http.expectOne((r) => r.method === 'PUT' && r.url === BOARD_LAYOUT_URL);
    expect(put.request.body.steps).toEqual([{ laneSurveyId: 's1', stepId: 's2', x: 120, y: 60 }]);
    http.expectNone(LAYOUT_URL);
    put.flush(null);
  });

  // WEB-BOARD-014
  it('faixa_reorganizarApagaSoAPosicaoDaFaixaNoQuadro', async () => {
    const { fixture, el, http } = await boardSetup();
    const automatic = businessLayout(FLOW).boxes.get('s3')!;

    Array.from(el.querySelectorAll<HTMLButtonElement>('.biz-bar button')).find((b) => b.textContent!.includes('Reorganizar'))!.click();
    fixture.detectChanges();
    Array.from(el.querySelectorAll<HTMLButtonElement>('app-confirm-dialog button')).find((b) => b.textContent!.includes('Reorganizar'))!.click();
    http.expectOne((r) => r.method === 'DELETE' && r.url === BOARD_LAYOUT_URL && r.params.get('lane') === 's1').flush(null);
    fixture.detectChanges();

    expect(positionOf(fixture, 's3')).toEqual({ x: automatic.x, y: automatic.y });
  });

  // WEB-BOARD-015
  it('faixa_fluxoIgualReenviadoPeloQuadro_naoRefazOQueAPessoaMexeu_eFluxoNovoEntra', async () => {
    const { fixture, host, node } = await boardSetup();

    nodeDebug(fixture, 's2').triggerEventHandler('fNodePositionChange', { x: 300, y: 200 });
    host.flow.set(JSON.parse(JSON.stringify(FLOW)));
    fixture.detectChanges();
    expect(positionOf(fixture, 's2')).toEqual({ x: 300, y: 200 });

    host.flow.set({ ...FLOW, steps: FLOW.steps.map((s) => (s.id === 's4' ? { ...s, label: 'Pedido finalizado' } : s)) });
    fixture.detectChanges();
    expect(node('s4').querySelector('.biz-text')!.textContent).toContain('Pedido finalizado');
    await afterDebounce();
    TestBed.inject(HttpTestingController).match((r) => r.method === 'PUT').forEach((r) => r.flush(null));
  });
});
