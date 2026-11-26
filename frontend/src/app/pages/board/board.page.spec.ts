import { Component, input } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { AiStatusService } from '../../core/ai-status.service';
import { AiProvider, AiUse, BoardDetail, BoardLane, BusinessFlow, BusinessKind, BusinessLayout, BusinessStep, LabelOrigin, LabelState, SourceLanguage, SurveyDetail, SurveyStatus } from '../../core/models';
import { BusinessExportData } from '../../core/graph/business-export';
import { businessLayout } from '../../core/graph/business-layout';
import { BusinessViewComponent } from '../survey/business-view.component';
import { BOARDS_DELETED_STATE } from '../boards/boards.page';
import { BOARD_FEEDBACK_MS, BoardPage } from './board.page';

// A visão de negócio da faixa tem teste próprio (business-view.component.spec): aqui só se confere o que a tela lhe entrega.
@Component({ selector: 'app-business-view', template: '<span class="stub-view">{{ boardId() }}</span>' })
class StubBusinessView {
  readonly detail = input.required<SurveyDetail>();
  readonly boardId = input<string | null>(null);
  readonly boardFlow = input<BusinessFlow | null>(null);
  readonly boardLayout = input<BusinessLayout | null>(null);

  // O que a visão de verdade entrega para a exportação: o desenho automático do fluxo da faixa.
  exportData(): BusinessExportData | null {
    const flow = this.boardFlow();
    return flow ? { surveyId: this.detail().survey.id, flow, geometry: businessLayout(flow), bends: new Map() } : null;
  }
}

const step = (id: string, kind: BusinessKind): BusinessStep => ({
  id,
  nodeId: `n-${id}`,
  kind,
  label: id,
  labelOrigin: LabelOrigin.Rule,
  labelState: LabelState.Confirmed,
  ref: null,
  sourceNodeIds: [],
  inferred: false,
  codeChanged: false,
});

const flow = (surveyId: string, kinds: BusinessKind[]): BusinessFlow => ({ surveyId, schemaVersion: 1, collapsedDecisions: 0, steps: kinds.map((k, i) => step(`p${i}`, k)), edges: [] });

const lane = (surveyId: string, order: number, extra: Partial<BoardLane> = {}): BoardLane => ({
  surveyId,
  order,
  title: `Faixa ${surveyId}`,
  entryPoint: `Ctl${surveyId}.Criar`,
  repositories: ['loja'],
  status: SurveyStatus.Completed,
  stale: false,
  flow: flow(surveyId, [BusinessKind.Start, BusinessKind.Decision]),
  ...extra,
});

const board = (lanes: BoardLane[], extra: Partial<BoardDetail> = {}): BoardDetail => ({ id: 'b1', name: 'Compra', glossary: null, lanes, layout: { steps: [], edges: [] }, ...extra });

const detailOf = (id: string, status = SurveyStatus.Completed): SurveyDetail => ({
  survey: {
    id,
    repository: 'loja',
    solutionPath: 'a.sln',
    language: SourceLanguage.DotNet,
    entryPoint: `Ctl${id}.Criar`,
    maxDepth: 7,
    status,
    schemaVersion: 1,
    nodeCount: 4,
    edgeCount: 3,
    unresolvedCount: 0,
    createdAt: '2026-11-26T10:00:00Z',
    crossRepositories: ['pagamentos'],
    segments: [],
  },
});

const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

async function setup(initial: BoardDetail = board([lane('s1', 0), lane('s2', 1)])) {
  TestBed.configureTestingModule({ imports: [BoardPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
  TestBed.overrideComponent(BoardPage, { remove: { imports: [BusinessViewComponent] }, add: { imports: [StubBusinessView] } });
  const fixture: ComponentFixture<BoardPage> = TestBed.createComponent(BoardPage);
  fixture.componentRef.setInput('id', 'b1');
  fixture.detectChanges();
  const http = TestBed.inject(HttpTestingController);
  http.expectOne('/api/boards/b1').flush(initial);
  for (const l of initial.lanes.filter((x) => x.status === SurveyStatus.Completed)) http.expectOne(`/api/surveys/${l.surveyId}`).flush(detailOf(l.surveyId));
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const button = (text: string) => Array.from(el.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent!.includes(text))!;
  const put = (): TestRequest => http.expectOne((r) => r.method === 'PUT' && r.url === '/api/boards/b1');
  return { fixture, el, http, button, put };
}

function withAi(fixture: ComponentFixture<BoardPage>): void {
  TestBed.inject(AiStatusService).status.set({ enabled: true, configured: true, provider: AiProvider.Claude, model: 'm', uses: [AiUse.Suggestions], tokensToday: 0, dailyTokenBudget: 0 });
  fixture.detectChanges();
}

describe('BoardPage (Fase 60)', () => {
  // WEB-BOARD-020
  it('quadroCarregado_desenhaUmaFaixaPorLevantamentoComTituloEntradaERepositorios', async () => {
    const { el } = await setup();

    const lanes = Array.from(el.querySelectorAll('.lane'));
    expect(lanes.length).toBe(2);
    expect((lanes[0].querySelector('.lane-title') as HTMLInputElement).value).toBe('Faixa s1');
    expect(lanes[0].querySelector('.lane-entry')!.textContent).toContain('Ctls1.Criar');
    expect(lanes[0].querySelector('.lane-repo')!.textContent).toContain('loja');
    expect(el.querySelectorAll('.stub-view').length).toBe(2);
    expect(el.querySelector('h1')!.textContent).toContain('Compra');
  });

  // WEB-BOARD-021
  it('legenda_listaSoOsTiposQueAparecemNoQuadro', async () => {
    const { el } = await setup();

    const items = Array.from(el.querySelectorAll('.board-legend li')).map((li) => li.textContent!.trim());
    expect(items.length).toBe(2);
    expect(items[0]).toContain('Início');
  });

  // WEB-BOARD-022
  it('faixaRodando_mostraLevantandoESemBotaoMorto_eOutraFaixaSegueDesenhada', async () => {
    const { el, http, fixture } = await setup(board([lane('s1', 0), lane('s2', 1, { status: SurveyStatus.Running, flow: null })]));

    const lanes = Array.from(el.querySelectorAll('.lane'));
    expect(lanes[1].textContent).toContain('Levantando');
    expect(lanes[1].querySelector('.walking')).not.toBeNull();
    expect(lanes[1].querySelector('.stub-view')).toBeNull();
    expect(lanes[0].querySelector('.stub-view')).not.toBeNull();
    fixture.destroy();
    http.match(() => true);
  });

  // WEB-BOARD-023
  it('faixaComFalha_mostraOAvisoELevantarDeNovo', async () => {
    const { el } = await setup(board([lane('s1', 0), lane('s2', 1, { status: SurveyStatus.Failed, flow: null })]));

    const second = el.querySelectorAll('.lane')[1];
    expect(second.querySelector('.notice.is-error')!.textContent).toContain('falhou');
    expect(second.textContent).toContain('Levantar de novo');
  });

  // WEB-BOARD-024
  it('renomearAFaixa_gravaOTituloNovoMantendoAOrdem', async () => {
    const { el, fixture, put } = await setup();
    const input = el.querySelectorAll<HTMLInputElement>('.lane-title')[1];

    input.value = 'Pagamento';
    input.dispatchEvent(new Event('blur'));
    const request = put();

    expect(request.request.body.lanes).toEqual([
      { surveyId: 's1', order: 0, title: 'Faixa s1' },
      { surveyId: 's2', order: 1, title: 'Pagamento' },
    ]);
    request.flush(board([lane('s1', 0), lane('s2', 1, { title: 'Pagamento' })]));
    fixture.detectChanges();
    expect(el.querySelectorAll<HTMLInputElement>('.lane-title')[1].value).toBe('Pagamento');
  });

  // WEB-BOARD-025
  it('tituloVazio_voltaAoAnteriorSemGravar', async () => {
    const { el, http } = await setup();
    const input = el.querySelectorAll<HTMLInputElement>('.lane-title')[0];

    input.value = '   ';
    input.dispatchEvent(new Event('blur'));

    expect(input.value).toBe('Faixa s1');
    http.expectNone((r) => r.method === 'PUT');
  });

  // WEB-BOARD-026
  it('moverParaADireita_gravaANovaOrdem_eAsPontasFicamDesabilitadas', async () => {
    const { el, fixture, put } = await setup();
    const left = el.querySelectorAll<HTMLButtonElement>('.lane-move')[0].querySelectorAll('button');
    expect(left[0].disabled).toBeTrue();

    left[1].click();
    const request = put();
    expect(request.request.body.lanes.map((l: { surveyId: string }) => l.surveyId)).toEqual(['s2', 's1']);
    request.flush(board([lane('s2', 0), lane('s1', 1)]));
    fixture.detectChanges();

    expect(Array.from(el.querySelectorAll<HTMLInputElement>('.lane-title')).map((i) => i.value)).toEqual(['Faixa s2', 'Faixa s1']);
  });

  // WEB-BOARD-027
  it('arrastarAFaixa_soltaNaOutraEGravaAOrdem', async () => {
    const { el, put } = await setup();
    const lanes = el.querySelectorAll('.lane');
    const dataTransfer = new DataTransfer();

    lanes[0].querySelector('.lane-grip')!.dispatchEvent(new DragEvent('dragstart', { dataTransfer, bubbles: true }));
    lanes[1].dispatchEvent(new DragEvent('dragover', { dataTransfer, bubbles: true, cancelable: true }));
    lanes[1].dispatchEvent(new DragEvent('drop', { dataTransfer, bubbles: true, cancelable: true }));

    expect(put().request.body.lanes.map((l: { surveyId: string }) => l.surveyId)).toEqual(['s2', 's1']);
  });

  // WEB-BOARD-028
  it('glossario_mostraADicaDoFormato_contaAsEntradasEGrava', async () => {
    const { el, fixture, put } = await setup(board([lane('s1', 0)], { glossary: 'Pedido: compra do cliente' }));
    const details = el.querySelector('details.board-glossary')!;
    const area = el.querySelector<HTMLTextAreaElement>('#board-glossary')!;

    expect(details.querySelector('summary')!.textContent).toContain('(1)');
    expect(el.querySelector('#board-glossary-help')!.textContent).toContain('Termo: significado');
    area.value = 'Pedido: compra do cliente\nSKU: código do produto';
    area.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    // UX-FIM-107: a contagem segue o salvo e o rascunho aparece como alteração não salva.
    expect(details.querySelector('summary')!.textContent).toContain('(1)');
    expect(details.querySelector('summary [role="status"]')).not.toBeNull();
    Array.from(el.querySelectorAll<HTMLButtonElement>('.board-glossary-actions button'))[0].click();

    const request = put();
    expect(request.request.body.glossary).toBe('Pedido: compra do cliente\nSKU: código do produto');
    request.flush(board([lane('s1', 0)], { glossary: 'Pedido: compra do cliente\nSKU: código do produto' }));
    fixture.detectChanges();
    expect(details.querySelector('summary')!.textContent).toContain('(2)');
    expect(details.querySelector('summary [role="status"]')).toBeNull();
  });

  // WEB-BOARD-029
  it('renomearOQuadro_gravaONomeNovo', async () => {
    const { el, fixture, button, put } = await setup();

    button('Renomear').click();
    fixture.detectChanges();
    await fixture.whenStable();
    const input = el.querySelector<HTMLInputElement>('#board-name')!;
    input.value = 'Compra completa';
    input.dispatchEvent(new Event('input'));
    el.querySelector<HTMLFormElement>('.board-rename')!.dispatchEvent(new Event('submit', { cancelable: true }));

    expect(put().request.body.name).toBe('Compra completa');
  });

  // WEB-BOARD-030
  it('sugerirTextos_soComIaEUsoSugestoes_eAtualizaOQuadroDepois', async () => {
    const { el, fixture, http } = await setup();
    expect(Array.from(el.querySelectorAll('.board-bar button')).some((b) => b.textContent!.includes('Sugerir textos'))).toBeFalse();

    withAi(fixture);
    Array.from(el.querySelectorAll<HTMLButtonElement>('.board-bar button')).find((b) => b.textContent!.includes('Sugerir textos'))!.click();
    http.expectOne({ method: 'POST', url: '/api/boards/b1/labels/suggest' }).flush({ proposed: 3, skipped: 1 });
    fixture.detectChanges();

    expect(el.querySelector('.toast')!.textContent).toContain('3 textos propostos');
    http.expectOne('/api/boards/b1').flush(board([lane('s1', 0), lane('s2', 1)]));
  });

  // WEB-BOARD-031
  it('sugerirTextosComIaDesligadaNoServidor_mostraOErroPeloCodigo', async () => {
    const { el, fixture, http } = await setup();
    withAi(fixture);

    Array.from(el.querySelectorAll<HTMLButtonElement>('.board-bar button')).find((b) => b.textContent!.includes('Sugerir textos'))!.click();
    http.expectOne('/api/boards/b1/labels/suggest').flush({ code: 'AI_DISABLED' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(el.querySelector('.toast')!.getAttribute('role')).toBe('alert');
    expect(el.querySelector('.toast')!.textContent).toContain('A IA está desligada');
  });

  // WEB-BOARD-032
  it('levantarDeNovo_criaOLevantamentoComOMesmoPedido_esperaTerminarETrocaAFaixaMostrandoOsPassosQueSairam', async () => {
    const { el, fixture, http, button } = await setup(board([lane('s1', 0, { stale: true }), lane('s2', 1)]));
    expect(el.querySelector('.pill.is-stale')!.textContent).toContain('O código mudou');

    button('Levantar de novo').click();
    fixture.detectChanges();
    http.expectOne('/api/surveys/s1').flush(detailOf('s1'));
    const create = http.expectOne({ method: 'POST', url: '/api/surveys' });
    expect(create.request.body).toEqual({ repository: 'loja', solutionPath: 'a.sln', entryPoint: 'Ctls1.Criar', maxDepth: 7, crossRepositories: ['pagamentos'] });
    create.flush({ ...detailOf('s9', SurveyStatus.Pending).survey });
    fixture.detectChanges();
    expect(el.querySelectorAll('.lane')[0].textContent).toContain('Levantando');
    expect(el.querySelectorAll('.lane')[0].querySelector('.stub-view')).toBeNull();

    await wait(20);
    http.expectOne('/api/surveys/s9').flush(detailOf('s9'));
    const replace = http.expectOne({ method: 'PUT', url: '/api/boards/b1/lanes/s1/replace' });
    expect(replace.request.body).toEqual({ newSurveyId: 's9' });
    replace.flush({ board: board([lane('s9', 0), lane('s2', 1)]), removedSteps: [{ stepId: 'old', nodeId: 'n-old', label: 'Estorno avisado' }] });
    fixture.detectChanges();
    http.expectOne('/api/surveys/s9').flush(detailOf('s9'));
    fixture.detectChanges();

    const notice = el.querySelector('.board-replaced')!;
    expect(notice.textContent).toContain('Estorno avisado');
    expect(notice.textContent).toContain('1 passo da versão anterior');
    expect(el.querySelectorAll('.stub-view').length).toBe(2);
    fixture.destroy();
    http.match(() => true);
  });

  // WEB-BOARD-033
  it('levantarDeNovoQueFalha_mantemAFaixaEAvisa', async () => {
    const { el, fixture, http, button } = await setup(board([lane('s1', 0, { stale: true })]));

    button('Levantar de novo').click();
    http.expectOne('/api/surveys/s1').flush(detailOf('s1'));
    http.expectOne({ method: 'POST', url: '/api/surveys' }).flush(detailOf('s9', SurveyStatus.Pending).survey);
    await wait(20);
    http.expectOne('/api/surveys/s9').flush(detailOf('s9', SurveyStatus.Failed));
    fixture.detectChanges();

    http.expectNone((r) => r.url.includes('/replace'));
    expect(el.querySelector('.toast')!.textContent).toContain('O levantamento novo falhou');
    expect(el.querySelector('.lane .stub-view')).not.toBeNull();
    fixture.destroy();
  });

  // WEB-BOARD-034
  it('tirarAFaixa_pedeConfirmacaoEGravaSemEla_eAUltimaNaoSai', async () => {
    const { el, fixture, http, put } = await setup();
    const remove = () => Array.from(el.querySelectorAll<HTMLButtonElement>('.lane-actions button')).filter((b) => b.textContent!.includes('Tirar do quadro'));

    remove()[1].click();
    fixture.detectChanges();
    expect(el.querySelector('app-confirm-dialog')).not.toBeNull();
    http.expectNone((r) => r.method === 'PUT');
    Array.from(el.querySelectorAll<HTMLButtonElement>('app-confirm-dialog button')).find((b) => b.textContent!.trim() === 'Tirar')!.click();
    const request = put();
    expect(request.request.body.lanes.map((l: { surveyId: string }) => l.surveyId)).toEqual(['s1']);
    request.flush(board([lane('s1', 0)]));
    fixture.detectChanges();

    expect(remove()[0].disabled).toBeTrue();
  });

  // WEB-BOARD-035
  it('adicionarLevantamento_listaSoOsProntosQueNaoEstaoNoQuadroEAdicionaNoFim', async () => {
    const { el, fixture, http, button } = await setup(board([lane('s1', 0)]));

    button('Adicionar levantamento').click();
    fixture.detectChanges();
    const free = { ...detailOf('s5').survey };
    http.expectOne('/api/surveys').flush([detailOf('s1').survey, free, { ...detailOf('s6').survey, status: SurveyStatus.Failed }]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const options = Array.from(el.querySelectorAll('#board-add-lane option')).map((o) => (o as HTMLOptionElement).value);
    expect(options).toEqual(['s5']);

    el.querySelector<HTMLFormElement>('.dialog form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    const add = http.expectOne({ method: 'POST', url: '/api/boards/b1/lanes' });
    expect(add.request.body).toEqual({ surveyId: 's5' });
    add.flush(board([lane('s1', 0), lane('s5', 1)]));
    http.expectOne('/api/surveys/s5').flush(detailOf('s5'));
    fixture.detectChanges();

    expect(el.querySelectorAll('.lane').length).toBe(2);
    expect(el.querySelector('.dialog')).toBeNull();
  });

  // WEB-BOARD-036
  it('excluirOQuadro_pedeConfirmacaoDeleVoltaParaALista', async () => {
    const { el, fixture, http, button } = await setup();
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    button('Excluir quadro').click();
    fixture.detectChanges();
    http.expectNone((r) => r.method === 'DELETE');
    Array.from(el.querySelectorAll<HTMLButtonElement>('app-confirm-dialog button')).find((b) => b.textContent!.trim() === 'Excluir')!.click();
    http.expectOne({ method: 'DELETE', url: '/api/boards/b1' }).flush(null);

    expect(navigate).toHaveBeenCalledWith(['/boards'], { state: { [BOARDS_DELETED_STATE]: 'Compra' } });
  });

  // WEB-BOARD-037
  it('quadroInexistente_mostraAMensagemDoCodigoComTentarDeNovo', async () => {
    TestBed.configureTestingModule({ imports: [BoardPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.overrideComponent(BoardPage, { remove: { imports: [BusinessViewComponent] }, add: { imports: [StubBusinessView] } });
    const fixture = TestBed.createComponent(BoardPage);
    fixture.componentRef.setInput('id', 'x');
    fixture.detectChanges();

    TestBed.inject(HttpTestingController).expectOne('/api/boards/x').flush({ code: 'BOARD_NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.notice.is-error')!.textContent).toContain('Este quadro não existe mais');
    expect(Array.from(el.querySelectorAll('button')).some((b) => b.textContent!.includes('Tentar de novo'))).toBeTrue();
  });

  // WEB-BOARD-038
  it('faixaPronta_entregaAVisaoOQuadroAFaixaEAPosicaoDela', async () => {
    const { fixture } = await setup(
      board([lane('s1', 0), lane('s2', 1)], { layout: { steps: [{ laneSurveyId: 's2', stepId: 'p1', x: 10, y: 20 }], edges: [] } }),
    );
    const views = fixture.debugElement.queryAll(By.directive(StubBusinessView)).map((d) => d.componentInstance as StubBusinessView);

    expect(views.map((v) => v.boardId())).toEqual(['b1', 'b1']);
    expect(views[0].boardLayout()).toEqual({ steps: [], edges: [] });
    expect(views[1].boardLayout()).toEqual({ steps: [{ stepId: 'p1', x: 10, y: 20 }], edges: [] });
    expect(views[1].boardFlow()!.surveyId).toBe('s2');
  });

  // Fase 61: exportar o quadro
  async function openExport() {
    const ctx = await setup(board([lane('s1', 0), lane('s2', 1)], { name: 'Compra / Cancelamento: "2026"', glossary: 'Pedido: compra do cliente' }));
    const exported: Blob[] = [];
    const names: string[] = [];
    spyOn(URL, 'createObjectURL').and.callFake((blob) => {
      exported.push(blob as Blob);
      return 'blob:teste';
    });
    spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (this: HTMLAnchorElement) {
      names.push(this.download);
    });
    const choose = (text: string) => {
      ctx.el.querySelector<HTMLButtonElement>('.board-bar-side .popover > button')!.click();
      ctx.fixture.detectChanges();
      Array.from(ctx.el.querySelectorAll<HTMLButtonElement>('#board-export-menu button')).find((b) => b.textContent!.includes(text))!.click();
      ctx.fixture.detectChanges();
    };
    return { ...ctx, exported, names, choose };
  }

  // WEB-BOARD-050
  it('menuExportar_listaCincoFormatos_eFechaComEsc', async () => {
    const { el, fixture } = await setup();
    el.querySelector<HTMLButtonElement>('.board-bar-side .popover > button')!.click();
    fixture.detectChanges();

    const items = Array.from(el.querySelectorAll<HTMLButtonElement>('#board-export-menu button')).map((b) => b.textContent!.trim());
    expect(items).toEqual(['Imagem PNG', 'Imagem SVG', 'Copiar como Mermaid', 'Baixar como Mermaid (.mmd)', 'Baixar para o draw.io (.drawio)']);
    el.querySelector('#board-export-menu')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    expect(el.querySelector('#board-export-menu')).toBeNull();
  });

  // WEB-BOARD-051
  it('exportarMermaid_baixa_umSubgraphPorFaixa_eONomeDoArquivoSaiDoNomeDoQuadro', async () => {
    const { exported, names, choose } = await openExport();

    choose('Baixar como Mermaid');

    const text = await exported[0].text();
    expect(text.startsWith('flowchart TD')).toBeTrue();
    expect(text.match(/subgraph /g)!.length).toBe(2);
    expect(text).toContain('["Faixa s1"]');
    expect(names).toEqual(['rastro-quadro-compra-cancelamento-2026.mmd']);
    expect(names[0]).not.toMatch(/[\\/:*?"<>|\s]/);
  });

  // WEB-BOARD-052
  it('exportarDrawio_buscaOsLinksDeCadaFaixa_eBaixaUmaMolduraPorFaixaComANotaDoGlossario', async () => {
    const { http, exported, names, choose, fixture } = await openExport();

    choose('draw.io');
    const links = http.match((r) => r.url.endsWith('/links'));
    expect(links.map((r) => r.request.url).sort()).toEqual(['/api/surveys/s1/links', '/api/surveys/s2/links']);
    links.forEach((r) => r.flush([]));
    fixture.detectChanges();

    const xml = await exported[0].text();
    const doc = new DOMParser().parseFromString(xml, 'application/xml');
    expect(doc.getElementsByTagName('parsererror').length).toBe(0);
    expect([...doc.querySelectorAll('mxCell[id^="f"]')].map((f) => f.getAttribute('value'))).toEqual(['Faixa s1', 'Faixa s2']);
    expect(xml).toContain('Pedido: compra do cliente');
    expect(names).toEqual(['rastro-quadro-compra-cancelamento-2026.drawio']);
  });

  // WEB-BOARD-053
  it('exportarDrawio_comARotaDeLinksForaDoAr_baixaOArquivoSemLink', async () => {
    const { http, exported, choose, fixture } = await openExport();

    choose('draw.io');
    http.match((r) => r.url.endsWith('/links')).forEach((r) => r.flush({ code: 'X' }, { status: 500, statusText: 'Erro' }));
    fixture.detectChanges();

    expect(exported.length).toBe(1);
    expect(await exported[0].text()).not.toContain(' link="');
  });

  // WEB-BOARD-054
  it('exportarSvg_baixaOSvgDoQuadroComAsFaixas', async () => {
    const { exported, names, choose } = await openExport();

    choose('Imagem SVG');

    const svg = await exported[0].text();
    expect(new DOMParser().parseFromString(svg, 'image/svg+xml').getElementsByTagName('parsererror').length).toBe(0);
    expect(svg).toContain('>Faixa s1<');
    expect(svg).toContain('>Faixa s2<');
    expect(svg).toContain('Pedido: compra do cliente');
    expect(names).toEqual(['rastro-quadro-compra-cancelamento-2026.svg']);
  });

  // WEB-BOARD-055
  it('exportarSemFaixaDesenhada_avisaEmVezDeBaixarArquivoVazio', async () => {
    const { el, fixture } = await setup(board([lane('s1', 0, { status: SurveyStatus.Running, flow: null })]));
    const download = spyOn(HTMLAnchorElement.prototype, 'click');
    el.querySelector<HTMLButtonElement>('.board-bar-side .popover > button')!.click();
    fixture.detectChanges();

    Array.from(el.querySelectorAll<HTMLButtonElement>('#board-export-menu button')).find((b) => b.textContent!.includes('Baixar como Mermaid'))!.click();
    fixture.detectChanges();

    expect(download).not.toHaveBeenCalled();
    expect(el.querySelector('.toast')!.textContent).toContain('ainda não tem faixa desenhada');
    fixture.destroy();
    TestBed.inject(HttpTestingController).match(() => true);
  });
  // Plano de UX do fim da visão de negócio (Fases B e C)
  async function settle(fixture: ComponentFixture<BoardPage>) {
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
  }

  // UX-FIM-201, UX-FIM-101
  it('moverComASeta_mantemOFocoNaSetaDaFaixaQueAndou_eAvisaAPosicao', async () => {
    const { el, fixture, put } = await setup();
    const arrows = (index: number) => el.querySelectorAll('.lane')[index].querySelectorAll<HTMLButtonElement>('.lane-move button');
    arrows(0)[1].focus();

    arrows(0)[1].click();
    fixture.detectChanges();
    // Durante a gravação a seta não fica desabilitada (o foco não cai), só marcada como ocupada.
    expect(arrows(0)[1].disabled).toBeFalse();
    expect(arrows(0)[1].getAttribute('aria-disabled')).toBe('true');
    arrows(0)[1].click();
    const request = put();
    request.flush(board([lane('s2', 0), lane('s1', 1)]));
    await settle(fixture);

    // A faixa s1 foi para o fim: a seta para a direita ficou desabilitada, então o foco vai à da esquerda.
    expect(document.activeElement).toBe(arrows(1)[0]);
    expect(el.querySelector('.toast')!.textContent).toContain('na posição');
  });

  // UX-FIM-202
  it('renomearOQuadro_focaEMarcaOCampo_escCancelaEOFocoVoltaAoBotao', async () => {
    const { el, fixture, button } = await setup();

    button('Renomear').click();
    await settle(fixture);
    const input = el.querySelector<HTMLInputElement>('#board-name')!;
    expect(document.activeElement).toBe(input);
    expect(input.selectionStart).toBe(0);
    expect(input.selectionEnd).toBe(input.value.length);

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle(fixture);

    expect(el.querySelector('#board-name')).toBeNull();
    expect(document.activeElement).toBe(el.querySelector('#board-rename-btn'));
  });

  // UX-FIM-202, UX-FIM-101
  it('renomearOQuadro_avisaEDevolveOFocoAoBotao', async () => {
    const { el, fixture, button, put } = await setup();

    button('Renomear').click();
    await settle(fixture);
    const input = el.querySelector<HTMLInputElement>('#board-name')!;
    input.value = 'Outro nome';
    input.dispatchEvent(new Event('input'));
    el.querySelector<HTMLFormElement>('.board-rename')!.dispatchEvent(new Event('submit', { cancelable: true }));
    put().flush(board([lane('s1', 0), lane('s2', 1)], { name: 'Outro nome' }));
    await settle(fixture);

    expect(el.querySelector('.toast')!.textContent).toContain('Quadro renomeado');
    expect(document.activeElement).toBe(el.querySelector('#board-rename-btn'));
  });

  // UX-FIM-101, UX-FIM-204
  it('renomearOTituloDaFaixaETirarFaixa_avisamDepoisDeGravar_eOFocoVaiParaAFaixaQueFicou', async () => {
    const { el, fixture, http, put } = await setup();
    const input = el.querySelectorAll<HTMLInputElement>('.lane-title')[1];

    input.value = 'Pagamento';
    input.dispatchEvent(new Event('blur'));
    put().flush(board([lane('s1', 0), lane('s2', 1, { title: 'Pagamento' })]));
    fixture.detectChanges();
    expect(el.querySelector('.toast')!.textContent).toContain('Faixa renomeada para');

    Array.from(el.querySelectorAll<HTMLButtonElement>('.lane-actions button')).filter((b) => b.textContent!.includes('Tirar do quadro'))[1].click();
    fixture.detectChanges();
    Array.from(el.querySelectorAll<HTMLButtonElement>('app-confirm-dialog button')).find((b) => b.textContent!.trim() === 'Tirar')!.click();
    put().flush(board([lane('s1', 0)]));
    await settle(fixture);

    expect(el.querySelector('.toast')!.textContent).toContain('tirada do quadro');
    expect(document.activeElement).toBe(el.querySelector('.lane-title'));
    http.match(() => true);
  });

  // UX-FIM-101, UX-FIM-204
  it('adicionarFaixa_avisaEDevolveOFocoAoBotaoAdicionar', async () => {
    const { el, fixture, http, button } = await setup(board([lane('s1', 0)]));

    button('Adicionar levantamento').click();
    fixture.detectChanges();
    http.expectOne('/api/surveys').flush([detailOf('s5').survey]);
    await settle(fixture);
    el.querySelector<HTMLFormElement>('.dialog form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectOne({ method: 'POST', url: '/api/boards/b1/lanes' }).flush(board([lane('s1', 0), lane('s5', 1)]));
    http.expectOne('/api/surveys/s5').flush(detailOf('s5'));
    await settle(fixture);

    expect(el.querySelector('.toast')!.textContent).toContain('Faixa adicionada ao quadro');
    expect(document.activeElement).toBe(el.querySelector('#board-add-btn'));
  });

  // UX-FIM-102
  it('erroDeGravacao_ficaAteAPessoaFechar', async () => {
    const { el, fixture, put } = await setup();
    const input = el.querySelectorAll<HTMLInputElement>('.lane-title')[0];
    input.value = 'Novo';
    input.dispatchEvent(new Event('blur'));
    jasmine.clock().install();
    try {
      put().flush({ code: 'BOARD_NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
      fixture.detectChanges();
      jasmine.clock().tick(BOARD_FEEDBACK_MS * 3);
      fixture.detectChanges();

      expect(el.querySelector('.toast.is-error')).not.toBeNull();
      el.querySelector<HTMLButtonElement>('.toast button')!.click();
      fixture.detectChanges();
      expect(el.querySelector('.toast')).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  // UX-FIM-203
  it('menuExportar_temAriaHasPopup_eOFocoVoltaAoBotaoAoFecharPorEscEPorCliqueFora', async () => {
    const { el, fixture } = await setup();
    const trigger = el.querySelector<HTMLButtonElement>('#board-export-btn')!;
    expect(trigger.getAttribute('aria-haspopup')).toBe('menu');

    trigger.click();
    await settle(fixture);
    expect(document.activeElement).toBe(el.querySelector('#board-export-menu button'));
    el.querySelector('#board-export-menu')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle(fixture);
    expect(document.activeElement).toBe(trigger);

    trigger.click();
    await settle(fixture);
    el.querySelector<HTMLElement>('.menu-backdrop')!.click();
    await settle(fixture);
    expect(el.querySelector('#board-export-menu')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  // UX-FIM-203
  it('menuExportar_tabFechaOMenuESeguePeloBotao', async () => {
    const { el, fixture } = await setup();
    const trigger = el.querySelector<HTMLButtonElement>('#board-export-btn')!;
    trigger.click();
    await settle(fixture);

    el.querySelector('#board-export-menu')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
    await settle(fixture);

    expect(el.querySelector('#board-export-menu')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  // UX-FIM-110
  it('menuExportarSemFaixaDesenhada_marcaOsItensComoDesabilitadosComADica', async () => {
    const { el, fixture } = await setup(board([lane('s1', 0, { status: SurveyStatus.Running, flow: null })]));
    el.querySelector<HTMLButtonElement>('#board-export-btn')!.click();
    fixture.detectChanges();

    const items = Array.from(el.querySelectorAll<HTMLButtonElement>('#board-export-menu button'));
    expect(items.every((b) => b.getAttribute('aria-disabled') === 'true')).toBeTrue();
    expect(items[0].title).toContain('Espere as faixas aparecerem para exportar');
    fixture.destroy();
    TestBed.inject(HttpTestingController).match(() => true);
  });

  // UX-FIM-205
  it('arrastar_destacaAFaixaDeDestinoEApagaNaSoltura', async () => {
    const { el, fixture, put } = await setup();
    const lanes = el.querySelectorAll('.lane');
    const dataTransfer = new DataTransfer();

    lanes[0].querySelector('.lane-grip')!.dispatchEvent(new DragEvent('dragstart', { dataTransfer, bubbles: true }));
    lanes[1].dispatchEvent(new DragEvent('dragover', { dataTransfer, bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(lanes[1].classList).toContain('is-drop-target');
    expect(lanes[0].classList).not.toContain('is-drop-target');

    lanes[1].dispatchEvent(new DragEvent('drop', { dataTransfer, bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(lanes[1].classList).not.toContain('is-drop-target');
    put();
  });

  // UX-FIM-103
  it('botoesDaFaixa_levamOTituloNoNomeAcessivel', async () => {
    const { el } = await setup(board([lane('s1', 0, { stale: true }), lane('s2', 1)]));

    const first = el.querySelectorAll('.lane')[0];
    expect(first.querySelector('a.btn')!.getAttribute('aria-label')).toContain('Abrir a faixa');
    expect(Array.from(first.querySelectorAll('.lane-actions button')).every((b) => !!b.getAttribute('aria-label'))).toBeTrue();
  });

  // UX-FIM-111
  it('voltarAosQuadros_temTitle', async () => {
    const { el } = await setup();

    expect(el.querySelector('.page-back')!.getAttribute('title')).toBeTruthy();
  });

  // UX-FIM-207, UX-FIM-113
  it('levantarDeNovo_avisaOInicioEMoveOFocoParaOEstadoDaFaixa', async () => {
    const { el, fixture, http, button } = await setup(board([lane('s1', 0, { stale: true }), lane('s2', 1)]));

    button('Levantar de novo').click();
    await settle(fixture);

    expect(el.querySelector('.toast')!.textContent).toContain('Levantando de novo a faixa');
    expect(document.activeElement).toBe(el.querySelector('[data-lane-status="s1"]'));
    // Uma região viva só para todos os estados de faixa, e nenhuma dentro da faixa.
    expect(el.querySelector('.lane-status[role="status"]')).toBeNull();
    expect(el.querySelector('.board-layout > .visually-hidden[role="status"]')!.textContent).toContain('Levantando a faixa');
    fixture.destroy();
    http.match(() => true);
  });

  // UX-FIM-108
  it('trocarDeQuadro_limpaOAvisoEOErroDoAnterior', async () => {
    const { el, fixture, http, put } = await setup();
    const input = el.querySelectorAll<HTMLInputElement>('.lane-title')[0];
    input.value = 'Novo';
    input.dispatchEvent(new Event('blur'));
    put().flush({ code: 'BOARD_NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(el.querySelector('.toast.is-error')).not.toBeNull();

    fixture.componentRef.setInput('id', 'b2');
    fixture.detectChanges();
    http.expectOne('/api/boards/b2');
    fixture.detectChanges();

    expect(el.querySelector('.toast')).toBeNull();
    fixture.destroy();
  });
});
