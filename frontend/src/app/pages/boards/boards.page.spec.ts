import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BoardDetail, BoardSummary, SourceLanguage, SurveyStatus, SurveySummary } from '../../core/models';
import { AddToBoardDialogComponent } from './add-to-board-dialog.component';
import { BoardCreateDialogComponent } from './board-create-dialog.component';
import { BOARDS_DELETED_STATE, BoardsPage } from './boards.page';

const summary = (id: string, name: string, laneCount = 2): BoardSummary => ({ id, name, laneCount, updatedAt: '2026-11-26T10:00:00Z' });

const survey = (id: string, status = SurveyStatus.Completed): SurveySummary => ({
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
  crossRepositories: [],
  segments: [],
});

const created = (id = 'b9'): BoardDetail => ({ id, name: 'Novo', glossary: null, lanes: [], layout: { steps: [], edges: [] } });

function setupList(boards: BoardSummary[] | { status: number }) {
  TestBed.configureTestingModule({ imports: [BoardsPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
  const fixture: ComponentFixture<BoardsPage> = TestBed.createComponent(BoardsPage);
  fixture.detectChanges();
  const http = TestBed.inject(HttpTestingController);
  const request = http.expectOne('/api/boards');
  if ('status' in boards) request.flush({ code: 'ERRO' }, { status: boards.status, statusText: 'Erro' });
  else request.flush(boards);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const button = (text: string) => Array.from(el.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent!.includes(text))!;
  return { fixture, el, http, button };
}

describe('BoardsPage (Fase 60)', () => {
  // WEB-BOARD-050
  it('lista_mostraUmaLinhaPorQuadroComFaixasELink', () => {
    const { el } = setupList([summary('b1', 'Compra', 3), summary('b2', 'Estorno', 1)]);

    const rows = Array.from(el.querySelectorAll('.board-row'));
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Compra');
    expect(rows[0].textContent).toContain('3 faixas');
    expect(rows[1].textContent).toContain('1 faixa');
    expect(rows[0].querySelector('a')!.getAttribute('href')).toBe('/boards/b1');
  });

  // WEB-BOARD-051
  it('semQuadros_mostraOVazioComOCaminho', () => {
    const { el } = setupList([]);

    expect(el.querySelector('.board-empty')!.textContent).toContain('Nenhum quadro ainda');
    expect(el.querySelector('.board-empty')!.textContent).toContain('um ou mais levantamentos');
  });

  // WEB-BOARD-051B (D-06)
  it('semQuadros_oVazioTemBotaoQueAbreODialogoELinkParaALista', () => {
    const { fixture, el, button, http } = setupList([]);

    button('Escolher levantamentos').click();
    http.expectOne('/api/surveys').flush([survey('s1')]);
    fixture.detectChanges();

    expect(el.querySelector('app-board-create-dialog')).not.toBeNull();
    expect(el.querySelector('.board-empty a')!.getAttribute('href')).toBe('/');
  });

  // WEB-BOARD-052
  it('falhaAoListar_mostraOErroComTentarDeNovo', () => {
    const { el, button, http } = setupList({ status: 500 });

    expect(el.querySelector('.notice.is-error')).not.toBeNull();
    button('Tentar de novo').click();
    http.expectOne('/api/boards').flush([]);
  });

  // WEB-BOARD-053
  it('excluir_pedeConfirmacaoApagaETiraALinha', () => {
    const { fixture, el, button, http } = setupList([summary('b1', 'Compra'), summary('b2', 'Estorno')]);

    el.querySelectorAll<HTMLButtonElement>('.board-row button')[0].click();
    fixture.detectChanges();
    expect(el.querySelector('app-confirm-dialog')!.textContent).toContain('Excluir o quadro "Compra"?');
    http.expectNone((r) => r.method === 'DELETE');
    Array.from(el.querySelectorAll<HTMLButtonElement>('app-confirm-dialog button')).find((b) => b.textContent!.trim() === 'Excluir')!.click();
    http.expectOne({ method: 'DELETE', url: '/api/boards/b1' }).flush(null);
    fixture.detectChanges();

    expect(el.querySelectorAll('.board-row').length).toBe(1);
    expect(el.textContent).toContain('Quadro "Compra" excluído.');
    expect(button).toBeDefined();
  });

  // WEB-BOARD-054
  it('excluirCancelado_naoApagaNada', () => {
    const { fixture, el, http } = setupList([summary('b1', 'Compra')]);

    el.querySelector<HTMLButtonElement>('.board-row button')!.click();
    fixture.detectChanges();
    Array.from(el.querySelectorAll<HTMLButtonElement>('app-confirm-dialog button')).find((b) => b.textContent!.includes('Cancelar'))!.click();
    fixture.detectChanges();

    http.expectNone((r) => r.method === 'DELETE');
    expect(el.querySelector('app-confirm-dialog')).toBeNull();
  });

  // WEB-BOARD-055
  it('novoQuadro_buscaOsLevantamentosEOCriarSoAceitaProntos_ENavegaParaOQuadro', async () => {
    const { fixture, el, button, http } = setupList([]);
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    button('Novo quadro').click();
    http.expectOne('/api/surveys').flush([survey('s1'), survey('s2'), survey('s3', SurveyStatus.Running)]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const picks = Array.from(el.querySelectorAll<HTMLInputElement>('.dialog .pick input'));
    expect(picks.length).toBe(2);
    el.querySelector<HTMLInputElement>('#board-create-name')!.value = 'Compra';
    el.querySelector<HTMLInputElement>('#board-create-name')!.dispatchEvent(new Event('input'));
    picks[1].click();
    picks[0].click();
    fixture.detectChanges();
    el.querySelector<HTMLFormElement>('.dialog form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    const post = http.expectOne({ method: 'POST', url: '/api/boards' });
    expect(post.request.body).toEqual({ name: 'Compra', surveyIds: ['s2', 's1'] });
    post.flush(created());

    expect(navigate).toHaveBeenCalledWith(['/boards', 'b9']);
  });

  // WEB-BOARD-056
  it('criarSemNomeEmLevantamento_mostraOsErrosEnaoEnvia', async () => {
    const { fixture, el, button, http } = setupList([]);

    button('Novo quadro').click();
    http.expectOne('/api/surveys').flush([survey('s1')]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    el.querySelector<HTMLFormElement>('.dialog form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();

    http.expectNone((r) => r.method === 'POST');
    expect(el.querySelector('#board-create-name-error')!.textContent).toContain('Informe o nome do quadro');
    expect(el.querySelector('#board-create-surveys-error')!.textContent).toContain('Escolha de 1 a 12');
  });

  // UX-FIM-206
  it('criar_navegaPrimeiro_eSoFechaODialogoQuandoATelaDoQuadroAbriu', async () => {
    const { fixture, el, button, http } = setupList([]);
    let finish!: (opened: boolean) => void;
    spyOn(TestBed.inject(Router), 'navigate').and.returnValue(new Promise<boolean>((resolve) => (finish = resolve)));
    button('Novo quadro').click();
    http.expectOne('/api/surveys').flush([survey('s1')]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    el.querySelector<HTMLInputElement>('#board-create-name')!.value = 'Compra';
    el.querySelector<HTMLInputElement>('#board-create-name')!.dispatchEvent(new Event('input'));
    el.querySelector<HTMLInputElement>('.dialog .pick input')!.click();
    el.querySelector<HTMLFormElement>('.dialog form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectOne({ method: 'POST', url: '/api/boards' }).flush(created());
    fixture.detectChanges();

    expect(el.querySelector('app-board-create-dialog')).not.toBeNull();
    finish(true);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('app-board-create-dialog')).toBeNull();
    expect(el.querySelector('.notice.is-error')).toBeNull();
  });

  // UX-FIM-206
  it('criarComNavegacaoCancelada_fechaODialogoEAvisaComOLinkParaOQuadro', async () => {
    const { fixture, el, button, http } = setupList([]);
    spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(false);
    button('Novo quadro').click();
    http.expectOne('/api/surveys').flush([survey('s1')]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    el.querySelector<HTMLInputElement>('#board-create-name')!.value = 'Compra';
    el.querySelector<HTMLInputElement>('#board-create-name')!.dispatchEvent(new Event('input'));
    el.querySelector<HTMLInputElement>('.dialog .pick input')!.click();
    el.querySelector<HTMLFormElement>('.dialog form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectOne({ method: 'POST', url: '/api/boards' }).flush(created());
    await fixture.whenStable();
    fixture.detectChanges();

    const notice = el.querySelector('.notice.is-error[role="alert"]')!;
    expect(notice).not.toBeNull();
    expect(notice.querySelector('a')!.getAttribute('href')).toBe('/boards/b9');
    expect(el.querySelector('app-board-create-dialog')).toBeNull();
  });

  // UX-FIM-106
  it('novoQuadro_mostraAbrindoEnquantoBuscaOsLevantamentos', () => {
    const { fixture, el, button, http } = setupList([]);

    button('Novo quadro').click();
    fixture.detectChanges();
    const opening = el.querySelector<HTMLButtonElement>('.board-head .btn-p')!;
    expect(opening.getAttribute('aria-busy')).toBe('true');
    expect(opening.getAttribute('aria-disabled')).toBe('true');
    opening.click();
    http.expectOne('/api/surveys').flush([]);
    fixture.detectChanges();

    expect(el.querySelector<HTMLButtonElement>('.board-head .btn-p')!.getAttribute('aria-busy')).toBe('false');
  });

  // UX-FIM-112
  it('erroAoExcluir_apareceSeparadoEMantemOsQuadrosNaTela', () => {
    const { fixture, el, http } = setupList([summary('b1', 'Compra'), summary('b2', 'Estorno')]);

    el.querySelectorAll<HTMLButtonElement>('.board-row button')[0].click();
    fixture.detectChanges();
    Array.from(el.querySelectorAll<HTMLButtonElement>('app-confirm-dialog button')).find((b) => b.textContent!.trim() === 'Excluir')!.click();
    http.expectOne({ method: 'DELETE', url: '/api/boards/b1' }).flush({ code: 'BOARD_NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(el.querySelector('.notice.is-error[role="alert"]')).not.toBeNull();
    expect(el.querySelectorAll('.board-row').length).toBe(2);
  });

  // UX-FIM-112
  it('vindoDaExclusaoPelaTelaDoQuadro_mostraOAvisoComONome', () => {
    const previous = history.state;
    history.replaceState({ ...previous, [BOARDS_DELETED_STATE]: 'Compra' }, '');
    try {
      const { el } = setupList([summary('b2', 'Estorno')]);

      expect(el.querySelector('.visually-hidden[role="status"]')!.textContent).toContain('Compra');
    } finally {
      history.replaceState(previous, '');
    }
  });
});

describe('BoardCreateDialogComponent (Fase 60)', () => {
  function open(preselected: string[], surveys: SurveySummary[]) {
    TestBed.configureTestingModule({ imports: [BoardCreateDialogComponent], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const fixture = TestBed.createComponent(BoardCreateDialogComponent);
    fixture.componentRef.setInput('surveys', surveys);
    fixture.componentRef.setInput('preselected', preselected);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, http: TestBed.inject(HttpTestingController) };
  }

  // WEB-BOARD-057
  it('escolhaFeitaNaLista_chegaMarcadaNaOrdemEOFalhaDaApiApareceNoDialogo', async () => {
    const { fixture, el, http } = open(['s2', 's1', 'sx'], [survey('s1'), survey('s2'), survey('s3', SurveyStatus.Failed)]);
    await fixture.whenStable();
    const boxes = Array.from(el.querySelectorAll<HTMLInputElement>('.pick input'));

    expect(boxes.map((b) => b.checked)).toEqual([true, true]);
    expect(el.querySelector('legend')!.textContent).toContain('(2 de 12)');
    el.querySelector<HTMLInputElement>('#board-create-name')!.value = 'Compra';
    el.querySelector<HTMLInputElement>('#board-create-name')!.dispatchEvent(new Event('input'));
    el.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    const post = http.expectOne({ method: 'POST', url: '/api/boards' });
    expect(post.request.body.surveyIds).toEqual(['s2', 's1']);
    post.flush({ code: 'BOARD_SURVEY_NOT_READY' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(el.querySelector('[role="alert"]')!.textContent).toContain('ainda não está pronto');
  });

  // UX-FIM-214
  it('cadaCaixaMarcada_mostraAPosicaoQueAFaixaVaiTer', async () => {
    const { fixture, el } = open(['s2', 's1'], [survey('s1'), survey('s2'), survey('s3')]);
    await fixture.whenStable();
    fixture.detectChanges();

    const positions = Array.from(el.querySelectorAll('.pick')).map((p) => p.querySelector('.pick-position')?.textContent?.trim() ?? null);
    expect(positions).toEqual(['2', '1', null]);
    expect(el.querySelector('.pick-position')!.getAttribute('aria-label')).toContain('no quadro');

    el.querySelectorAll<HTMLInputElement>('.pick input')[2].click();
    fixture.detectChanges();
    expect(el.querySelectorAll('.pick-position')[2].textContent!.trim()).toBe('3');
  });

  // UX-FIM-104
  it('erroDeCampo_ligaAMensagemAoCampo_focaOCampoEUsaRoleAlert', async () => {
    const { fixture, el } = open([], [survey('s1')]);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('#board-create-name')!.getAttribute('aria-describedby')).toBeNull();

    el.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('#board-create-name-error')!.getAttribute('role')).toBe('alert');
    expect(el.querySelector('#board-create-name')!.getAttribute('aria-describedby')).toBe('board-create-name-error');
    expect(document.activeElement).toBe(el.querySelector('#board-create-name'));
    expect(el.querySelector('fieldset')!.getAttribute('aria-describedby')).toBe('board-create-surveys-error');
  });

  // UX-FIM-104
  it('semLevantamentoPronto_oBotaoCriarFicaDesabilitado', async () => {
    const { fixture, el } = open([], [survey('s3', SurveyStatus.Failed)]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBeTrue();
  });
});

describe('AddToBoardDialogComponent (Fase 60)', () => {
  function open() {
    TestBed.configureTestingModule({ imports: [AddToBoardDialogComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(AddToBoardDialogComponent);
    fixture.componentRef.setInput('surveyId', 's1');
    fixture.componentRef.setInput('entryPoint', 'Ctls1.Criar');
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, http: TestBed.inject(HttpTestingController) };
  }

  // WEB-BOARD-058
  it('quadroExistente_adicionaAFaixaNoFimEOferecePararAbrir', async () => {
    const { fixture, el, http } = open();
    http.expectOne('/api/boards').flush([summary('b1', 'Compra'), summary('b2', 'Estorno')]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(Array.from(el.querySelectorAll('#add-board-choice option')).map((o) => o.textContent!.trim())).toEqual(['Compra', 'Estorno', 'Criar um quadro novo']);
    expect((el.querySelector('#add-board-choice') as HTMLSelectElement).value).toBe('b1');
    el.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    const post = http.expectOne({ method: 'POST', url: '/api/boards/b1/lanes' });
    expect(post.request.body).toEqual({ surveyId: 's1' });
    post.flush({ ...created('b1'), name: 'Compra' });
    fixture.detectChanges();

    expect(el.querySelector('.note-ok')!.textContent).toContain('Adicionado ao quadro "Compra"');
    expect(el.querySelector('a.btn-p')!.getAttribute('href')).toBe('/boards/b1');
    // UX-FIM-204: o formulário sumiu, o foco vai para "Abrir o quadro".
    await fixture.whenStable();
    expect(document.activeElement).toBe(el.querySelector('a.btn-p'));
  });

  // WEB-BOARD-059
  it('semQuadros_cria_umQuadroNovoSoComEsteLevantamento_eExigeONome', async () => {
    const { fixture, el, http } = open();
    http.expectOne('/api/boards').flush([]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.textContent).toContain('Você ainda não tem quadros');
    el.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
    http.expectNone((r) => r.method === 'POST');
    expect(el.querySelector('#add-board-name-error')!.textContent).toContain('Informe o nome do quadro');
    expect(el.querySelector('#add-board-name-error')!.getAttribute('role')).toBe('alert');
    expect(el.querySelector('#add-board-name')!.getAttribute('aria-describedby')).toBe('add-board-name-error');

    el.querySelector<HTMLInputElement>('#add-board-name')!.value = 'Compra';
    el.querySelector<HTMLInputElement>('#add-board-name')!.dispatchEvent(new Event('input'));
    el.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    const post = http.expectOne({ method: 'POST', url: '/api/boards' });
    expect(post.request.body).toEqual({ name: 'Compra', surveyIds: ['s1'] });
    post.flush(created());
  });

  // WEB-BOARD-060
  it('quadroJaTemOLevantamento_mostraOErroPeloCodigo', async () => {
    const { fixture, el, http } = open();
    http.expectOne('/api/boards').flush([summary('b1', 'Compra')]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    el.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectOne('/api/boards/b1/lanes').flush({ code: 'BOARD_LANE_EXISTS' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(el.querySelector('.field-error[role="alert"]')!.textContent).toContain('já está no quadro');
  });

  // WEB-BOARD-061 (D-08)
  it('botaoDeEnviar_dizOQuadroDeDestinoEMudaParaCriarQuandoANovaEstaEscolhida', async () => {
    const { fixture, el, http } = open();
    http.expectOne('/api/boards').flush([summary('b1', 'Compra'), summary('b2', 'Estorno')]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const submit = () => el.querySelector<HTMLButtonElement>('button[type="submit"]')!.textContent!.trim();
    expect(submit()).toBe('Adicionar a "Compra"');
    const select = el.querySelector<HTMLSelectElement>('#add-board-choice')!;
    select.value = 'new';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(submit()).toBe('Criar quadro e adicionar');
  });
});
