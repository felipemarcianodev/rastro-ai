import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BusinessFlow, BusinessKind, CheckStatus, EdgeConfidence, FlowGraph, LabelOrigin, LabelState, NodeKind, SourceLanguage, SurveyDetail, SurveyStatus, ValidationStatus, ValidationVerdict, ValidationView } from '../../core/models';
import { EDIT_HINT_SEEN_KEY, SurveyPage } from './survey.page';

const SURVEY_ID = 's1';

// Grafo do teste: a entrada, `unresolved` buracos e `methods` métodos que batem com a busca "Pedido".
let graphOverride: FlowGraph | undefined;

function graphWith(methods: number, unresolved: number): FlowGraph {
  const nodes = [
    { id: 'entry', kind: NodeKind.Entry, label: 'PedidoService.Criar' },
    ...Array.from({ length: methods }, (_, i) => ({ id: `m${i}`, kind: NodeKind.Method, label: `PedidoRepo${i}.Salvar` })),
    ...Array.from({ length: unresolved }, (_, i) => ({ id: `u${i}`, kind: NodeKind.Unresolved, label: `notifier.Notify${i}` })),
  ];
  const edges = nodes.slice(1).map((n, i) => ({ from: 'entry', to: n.id, order: i, confidence: EdgeConfidence.Extracted }));
  return { schemaVersion: 3, entryPoint: 'PedidoService.Criar', entryNodeId: 'entry', nodes, edges };
}

function detail(status: SurveyStatus): SurveyDetail {
  return {
    graph: graphOverride,
    survey: {
      id: SURVEY_ID,
      repository: 'LojaBanco',
      solutionPath: 'src/Loja.slnx',
      language: SourceLanguage.DotNet,
      entryPoint: 'PedidoService.Criar',
      maxDepth: 20,
      commitHash: 'abcdef1234567890',
      branch: 'main',
      status,
      schemaVersion: 1,
      nodeCount: 3,
      edgeCount: 2,
      unresolvedCount: graphOverride ? graphOverride.nodes.filter((n) => n.kind === NodeKind.Unresolved).length : 0,
      createdAt: '2026-11-06T10:00:00Z',
      crossRepositories: [],
      segments: [],
    },
  };
}

const validation: ValidationView = {
  id: 'v1',
  surveyId: SURVEY_ID,
  status: ValidationStatus.Completed,
  createdAt: '2026-11-06T11:00:00Z',
  hasDocument: false,
  result: {
    expectations: [],
    unexpected: [],
    decisionPointsBefore: 1,
    decisionPointsAfter: 1,
    build: { description: 'build', status: CheckStatus.Passed },
    tests: { description: 'testes', status: CheckStatus.Passed },
    verdict: ValidationVerdict.Approved,
    score: 82,
  },
};

// Responde a carga do levantamento e o que a tela pede depois dele.
// O último fixture aberto: os testes que clicam precisam rodar a detecção de mudanças dele.
let lastFixture: ComponentFixture<SurveyPage> | null = null;

function open(status: SurveyStatus, validations: ValidationView[] = []): HTMLElement {
  TestBed.configureTestingModule({ imports: [SurveyPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
  const fixture = TestBed.createComponent(SurveyPage);
  fixture.componentRef.setInput('id', SURVEY_ID);
  lastFixture = fixture;
  fixture.detectChanges();
  const http = TestBed.inject(HttpTestingController);
  const answer = (request: TestRequest) => {
    const url = request.request.url;
    if (url.endsWith(`/api/surveys/${SURVEY_ID}`)) request.flush(detail(status));
    else if (url.endsWith('/validations')) request.flush(validations);
    else if (url.endsWith('/draft') || url.endsWith('/staleness')) request.flush(null);
    else request.flush([]);
  };
  http.match(() => true).forEach(answer);
  fixture.detectChanges();
  http.match(() => true).forEach(answer);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

// Visão de negócio: o clique no seletor, a resposta da rota e a espera até o desenho existir.
// O grafo do teste tem a entrada e `m0`: dois passos-chave, com a decisão no segundo.
const BUSINESS_FLOW: BusinessFlow = {
  surveyId: SURVEY_ID,
  schemaVersion: 1,
  collapsedDecisions: 0,
  steps: [
    { id: 'b1', nodeId: 'entry', kind: BusinessKind.Start, label: 'Pedido recebido', labelOrigin: LabelOrigin.Rule, labelState: LabelState.Confirmed, ref: null, sourceNodeIds: ['entry'], inferred: false, codeChanged: false },
    { id: 'b2', nodeId: 'm0', kind: BusinessKind.Decision, label: 'O pedido é válido?', labelOrigin: LabelOrigin.Rule, labelState: LabelState.Confirmed, ref: null, sourceNodeIds: ['m0'], inferred: false, codeChanged: false },
  ],
  edges: [{ from: 'b1', to: 'b2', label: null, inferred: false }],
};

async function showBusiness(el: HTMLElement): Promise<void> {
  const items = [...el.querySelectorAll<HTMLButtonElement>('.segmented button')];
  items[3].click();
  lastFixture!.detectChanges();
  const http = TestBed.inject(HttpTestingController);
  http.match((r) => r.url.endsWith('/business')).forEach((r) => r.flush(BUSINESS_FLOW));
  http.match((r) => r.url.endsWith('/links')).forEach((r) => r.flush([]));
  http.match((r) => r.url.endsWith('/business/layout')).forEach((r) => r.flush({ steps: [], edges: [] }));
  lastFixture!.detectChanges();
  await lastFixture!.whenStable();
  lastFixture!.detectChanges();
}

describe('SurveyPage', () => {
  afterEach(() => {
    graphOverride = undefined;
  });

  it('mostra o botão Mapa do banco com a rota do repositório, em qualquer status', () => {
    // UX-F01
    for (const status of [SurveyStatus.Pending, SurveyStatus.Completed]) {
      TestBed.resetTestingModule();
      const el = open(status);

      const link = Array.from(el.querySelectorAll<HTMLAnchorElement>('.page-bar a.btn')).find((a) => a.textContent?.trim() === 'Mapa do banco');
      expect(link).withContext(`status ${status}`).toBeDefined();
      expect(link!.getAttribute('href')).toBe('/repositorios/LojaBanco/banco?levantamento=s1');
      expect(link!.classList.contains('btn-small')).toBeTrue();
      expect(el.querySelector('.page-bar-meta a')).toBeNull();
    }
  });

  // UX-G-F11
  it('Excluir levantamento… mora no fim da visão geral do painel, com o estilo quieto de perigo, e abre a confirmação', () => {
    TestBed.resetTestingModule();
    graphOverride = graphWith(1, 0);
    try {
      const el = open(SurveyStatus.Completed);
      const remove = el.querySelector<HTMLButtonElement>('app-node-panel .overview-danger button')!;
      expect(remove.textContent!.trim()).toBe('Excluir levantamento…');
      expect(remove.classList.contains('btn-danger-quiet')).toBeTrue();
      remove.click();
      lastFixture!.detectChanges();
      expect(el.querySelector('.overlay')).not.toBeNull();
    } finally {
      graphOverride = undefined;
    }
  });

  // UX-G-F09
  it('a dica da edição abre sozinha na primeira vez, cita o teclado e não volta na segunda', () => {
    localStorage.removeItem(EDIT_HINT_SEEN_KEY);
    graphOverride = graphWith(1, 0);
    try {
      for (const first of [true, false]) {
        TestBed.resetTestingModule();
        const el = open(SurveyStatus.Completed);
        const fixture = lastFixture!;
        (fixture.componentInstance as unknown as { startEditing(): void }).startEditing();
        const http = TestBed.inject(HttpTestingController);
        http.match(() => true).forEach((r) => r.flush(null));
        fixture.detectChanges();
        const hint = el.querySelector('#edit-hint');
        if (first) {
          expect(hint).not.toBeNull();
          expect(hint!.textContent).toContain('Pelo teclado: escolha o passo e use "Seguir para outro passo" no painel.');
          expect(localStorage.getItem(EDIT_HINT_SEEN_KEY)).toBe('true');
        } else {
          expect(hint).toBeNull();
        }
      }
    } finally {
      graphOverride = undefined;
      localStorage.removeItem(EDIT_HINT_SEEN_KEY);
    }
  });

  // UX-G-F04
  it('o cabeçalho conta passos, não nós', () => {
    TestBed.resetTestingModule();
    const el = open(SurveyStatus.Completed);
    const meta = el.querySelector('.page-bar-meta')!.textContent!;
    expect(meta).toContain('3 passos');
    expect(meta).not.toMatch(/nós?/);
  });

  // UX-G-F03
  it('a contagem de não resolvidos do cabeçalho é botão e, de outra visão, volta ao grafo e leva ao buraco', fakeAsync(() => {
    TestBed.resetTestingModule();
    graphOverride = graphWith(2, 2);
    try {
      const el = open(SurveyStatus.Completed);
      const fixture = lastFixture!;
      const button = el.querySelector<HTMLButtonElement>('.page-bar-meta button.has-holes')!;
      expect(button).not.toBeNull();
      expect(button.textContent!.trim()).toBe('2 não resolvidos');

      // Vai para a visão consolidada: o botão ainda leva ao grafo e ao primeiro buraco.
      [...el.querySelectorAll<HTMLButtonElement>('.segmented button')][2].click();
      fixture.detectChanges();
      expect(el.querySelector('app-flow-canvas')).toBeNull();
      el.querySelector<HTMLButtonElement>('.page-bar-meta button.has-holes')!.click();
      fixture.detectChanges();
      tick(500);
      fixture.detectChanges();
      expect(el.querySelector('app-flow-canvas')).not.toBeNull();
      expect((fixture.componentInstance as unknown as { selectedId(): string | null }).selectedId()).toBe('u0');
      fixture.destroy();
      tick(1000);
    } finally {
      graphOverride = undefined;
      localStorage.removeItem('rastro.layoutDirection');
    }
  }));

  it('sem não resolvidos a contagem é só texto', () => {
    // UX-G-F03
    TestBed.resetTestingModule();
    const el = open(SurveyStatus.Completed);
    expect(el.querySelector('.page-bar-meta button.has-holes')).toBeNull();
    expect(el.querySelector('.page-bar-meta')!.textContent).toContain('0 não resolvidos');
  });

  // UX-G-F22
  it('a busca lista até 8 ocorrências clicáveis e avisa que Esc fecha', fakeAsync(() => {
    TestBed.resetTestingModule();
    graphOverride = graphWith(12, 0);
    try {
      const el = open(SurveyStatus.Completed);
      const fixture = lastFixture!;
      const page = fixture.componentInstance as unknown as { openSearch(): void; onSearchInput(q: string): void; selectedId(): string | null };
      page.openSearch();
      page.onSearchInput('Pedido');
      fixture.detectChanges();
      tick(500);

      expect(el.querySelector('.graph-search .search-hint')!.textContent!.trim()).toBe('Esc fecha');
      const hits = [...el.querySelectorAll<HTMLButtonElement>('.search-results .search-hit')];
      expect(hits.length).toBe(8);
      expect(hits[0].textContent).toContain('Criar');
      expect(hits[0].textContent).toContain('PedidoService');
      hits[3].click();
      fixture.detectChanges();
      expect(page.selectedId()).toBe('m2');
      tick(1000);
      fixture.destroy();
    } finally {
      graphOverride = undefined;
    }
  }));

  it('diz a nota da última validação com a escala', () => {
    // UX-F15
    const el = open(SurveyStatus.Completed, [validation]);

    const button = Array.from(el.querySelectorAll<HTMLButtonElement>('.page-bar-side button')).find((b) => b.textContent?.includes('validação'));
    expect(button?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Última validação: Aprovada, nota 82 de 100');
  });

  it('mostra o commit curto na linha de metadados e deixa o branch em Detalhes', () => {
    // UX-F22
    const el = open(SurveyStatus.Completed);

    const meta = el.querySelector('.page-bar-meta') as HTMLElement;
    expect(meta.textContent).toContain('abcdef1');
    expect(meta.querySelector('.meta-details-body')?.textContent).toContain('main');
  });

  // WEB-UXC-010 (F09)
  it('o seletor tem as quatro visões, com a atual marcada e a troca pelo clique', () => {
    TestBed.resetTestingModule();
    const el = open(SurveyStatus.Completed);

    const group = el.querySelector('.segmented')!;
    expect(group.getAttribute('role')).toBe('group');
    const items = [...group.querySelectorAll<HTMLButtonElement>('button')];
    expect(items.map((b) => b.textContent!.trim())).toEqual(['Grafo', 'Mapa por sistemas', 'Visão consolidada', 'Visão de negócio']);
    expect(items.map((b) => b.getAttribute('aria-pressed'))).toEqual(['true', 'false', 'false', 'false']);

    items[2].click();
    lastFixture!.detectChanges();
    const after = [...el.querySelectorAll<HTMLButtonElement>('.segmented button')];
    expect(after.map((b) => b.getAttribute('aria-pressed'))).toEqual(['false', 'false', 'true', 'false']);
    localStorage.removeItem('rastro.layoutDirection');
  });

  // WEB-BIZV-030 (Fase 57)
  it('visaoDeNegocio_selecionadaNoSeletor_mostraOComponenteEMarcaOBotao', async () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    graphOverride = graphWith(1, 0);
    const el = open(SurveyStatus.Completed);

    await showBusiness(el);

    const items = [...el.querySelectorAll<HTMLButtonElement>('.segmented button')];
    expect(items.map((b) => b.getAttribute('aria-pressed'))).toEqual(['false', 'false', 'false', 'true']);
    expect(el.querySelector('app-business-view')).not.toBeNull();
    expect(el.querySelector('app-flow-canvas')).toBeNull();
    expect(el.querySelectorAll('.biz-node').length).toBe(2);
    localStorage.removeItem('rastro.layoutDirection');
  });

  // WEB-BIZV-031 (Fase 57)
  it('verNoGrafo_voltaAoCanvasComOPassoEscolhido', async () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    graphOverride = graphWith(1, 0);
    const el = open(SurveyStatus.Completed);
    await showBusiness(el);

    el.querySelector<HTMLElement>('[data-step-id="b2"]')!.click();
    lastFixture!.detectChanges();
    const button = [...el.querySelectorAll<HTMLButtonElement>('.biz-panel button')].find((b) => b.textContent!.includes('Ver no grafo'))!;
    button.click();
    lastFixture!.detectChanges();

    expect(el.querySelector('app-business-view')).toBeNull();
    expect(el.querySelector('app-flow-canvas')).not.toBeNull();
    expect(el.querySelector('app-node-panel')!.textContent).toContain('PedidoRepo0.Salvar');
    localStorage.removeItem('rastro.layoutDirection');
  });

  // WEB-BIZV-073 (Fase 59)
  it('menuDoNo_mostrarNaVisaoDeNegocio_soApareceParaONoQueAindaNaoEPassoChaveEGravaOTipoPasso', async () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    graphOverride = graphWith(2, 0);
    const el = open(SurveyStatus.Completed);
    const http = TestBed.inject(HttpTestingController);
    const page = lastFixture!.componentInstance as unknown as { openMenu(menu: { nodeId: string; x: number; y: number }): void };
    const hasItem = () => [...el.querySelectorAll('.context-menu button')].some((b) => b.textContent!.includes('Mostrar na visão de negócio'));
    const answerBusiness = () => {
      http.match((r) => r.url.endsWith('/business')).forEach((r) => r.flush(BUSINESS_FLOW));
      lastFixture!.detectChanges();
    };

    // Antes de a visão responder, o item não aparece; e a entrada já é passo-chave.
    page.openMenu({ nodeId: 'entry', x: 10, y: 10 });
    lastFixture!.detectChanges();
    expect(hasItem()).toBeFalse();
    answerBusiness();
    expect(hasItem()).toBeFalse();

    page.openMenu({ nodeId: 'm1', x: 10, y: 10 });
    lastFixture!.detectChanges();
    answerBusiness();
    expect(hasItem()).toBeTrue();

    [...el.querySelectorAll<HTMLButtonElement>('.context-menu button')].find((b) => b.textContent!.includes('Mostrar na visão de negócio'))!.click();
    const put = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/business/steps/m1/kind'));
    expect(put.request.body).toEqual({ kind: BusinessKind.Step });
    put.flush(BUSINESS_FLOW);
    lastFixture!.detectChanges();

    expect(el.querySelector('.context-menu')).toBeNull();
    expect(el.querySelector('.menu-toast')!.textContent).toContain('Passo incluído na visão de negócio');
    localStorage.removeItem('rastro.layoutDirection');
  });

  // WEB-BIZV-034 (Fase 57)
  it('menuExportar_comAVisaoDeNegocio_deixaTodosOsFormatosHabilitados', async () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    graphOverride = graphWith(1, 0);
    const el = open(SurveyStatus.Completed);
    await showBusiness(el);

    [...el.querySelectorAll<HTMLButtonElement>('.bar-group button')].find((b) => b.textContent!.trim() === 'Exportar')!.click();
    lastFixture!.detectChanges();
    const items = [...el.querySelectorAll<HTMLButtonElement>('#export-menu button')];

    expect(items.length).toBe(6);
    for (const item of items) {
      expect(item.disabled).withContext(item.textContent!).toBeFalse();
      expect(item.hasAttribute('aria-disabled')).withContext(item.textContent!).toBeFalse();
    }
    localStorage.removeItem('rastro.layoutDirection');
  });

  // WEB-BIZV-035 (Fase 61)
  it('baixarComoSvgEMermaid_comAVisaoDeNegocioNaTela_geraOsArquivosDaVisao', async () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    graphOverride = graphWith(1, 0);
    const el = open(SurveyStatus.Completed);
    await showBusiness(el);
    const exported: Blob[] = [];
    spyOn(URL, 'createObjectURL').and.callFake((blob) => {
      exported.push(blob as Blob);
      return 'blob:teste';
    });
    spyOn(HTMLAnchorElement.prototype, 'click');
    const choose = (text: string) => {
      [...el.querySelectorAll<HTMLButtonElement>('.bar-group button')].find((b) => b.textContent!.trim() === 'Exportar')!.click();
      lastFixture!.detectChanges();
      [...el.querySelectorAll<HTMLButtonElement>('#export-menu button')].find((b) => b.textContent!.includes(text))!.click();
      lastFixture!.detectChanges();
    };

    choose('Imagem SVG');
    choose('Baixar como Mermaid');

    const svg = await exported[0].text();
    expect(svg).toContain('<svg');
    expect(svg).toContain('O pedido é válido?');
    expect(new DOMParser().parseFromString(svg, 'image/svg+xml').getElementsByTagName('parsererror').length).toBe(0);
    const mermaid = await exported[1].text();
    expect(mermaid.startsWith('flowchart TD')).toBeTrue();
    expect(mermaid).toContain('O pedido é válido?');
    localStorage.removeItem('rastro.layoutDirection');
  });

  // WEB-UXD-009 (D-09)
  it('baixarComoSvg_onomeDoArquivoSaiDoI18n_comPrefixoEoNomeDaEntrada', async () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    graphOverride = graphWith(1, 0);
    const el = open(SurveyStatus.Completed);
    await showBusiness(el);
    spyOn(URL, 'createObjectURL').and.returnValue('blob:teste');
    const names: string[] = [];
    spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (this: HTMLAnchorElement) {
      names.push(this.download);
    });

    [...el.querySelectorAll<HTMLButtonElement>('.bar-group button')].find((b) => b.textContent!.trim() === 'Exportar')!.click();
    lastFixture!.detectChanges();
    [...el.querySelectorAll<HTMLButtonElement>('#export-menu button')].find((b) => b.textContent!.includes('Imagem SVG'))!.click();
    lastFixture!.detectChanges();

    expect(names.length).toBe(1);
    expect(names[0]).toMatch(/^rastro-.+\.svg$/);
    expect(names[0]).not.toContain('recorte');
    localStorage.removeItem('rastro.layoutDirection');
  });

  // WEB-BIZV-032 (Fase 57)
  it('baixarParaODrawio_comAVisaoDeNegocioNaTela_exportaOLosangoDaDecisao', async () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    graphOverride = graphWith(1, 0);
    const el = open(SurveyStatus.Completed);
    await showBusiness(el);
    let exported: Blob | null = null;
    spyOn(URL, 'createObjectURL').and.callFake((blob) => {
      exported = blob as Blob;
      return 'blob:teste';
    });
    spyOn(HTMLAnchorElement.prototype, 'click');

    [...el.querySelectorAll<HTMLButtonElement>('.bar-group button')].find((b) => b.textContent!.trim() === 'Exportar')!.click();
    lastFixture!.detectChanges();
    [...el.querySelectorAll<HTMLButtonElement>('#export-menu button')].find((b) => b.textContent!.includes('draw.io'))!.click();
    TestBed.inject(HttpTestingController).match((r) => r.url.endsWith('/links')).forEach((r) => r.flush([]));
    lastFixture!.detectChanges();

    const text = await (exported as unknown as Blob).text();
    expect(text).toContain('rhombus');
    expect(text).toContain('O pedido é válido?');
    localStorage.removeItem('rastro.layoutDirection');
  });

  // WEB-UXC-011 (F08, F07, F06)
  // UX-G-F11
  it('a barra tem os grupos Ver, Compartilhar e Alterar, com Comparar e Exportar separados e sem o Excluir', () => {
    TestBed.resetTestingModule();
    const el = open(SurveyStatus.Completed);

    const groups = [...el.querySelectorAll('.page-bar-side .bar-group')].map((g) => g.getAttribute('aria-label'));
    expect(groups).toEqual(['Ver', 'Compartilhar', 'Alterar']);
    const share = [...el.querySelectorAll<HTMLButtonElement>('.bar-group[aria-label="Compartilhar"] button')];
    expect(share.map((b) => b.textContent!.trim())).toEqual(['Comparar', 'Exportar', 'Adicionar a um quadro']);
    expect(share[1].title).toBe('PNG, SVG, Mermaid, draw.io e C4');
    expect(el.querySelector('.bar-group[aria-label="Alterar"] .btn-p')!.textContent!.trim()).toBe('Editar fluxo');
    const remove = [...el.querySelectorAll<HTMLButtonElement>('.page-bar-side button')].find((b) => b.textContent!.trim() === 'Excluir levantamento…');
    expect(remove).toBeUndefined();
    expect([...el.querySelectorAll('.page-bar-side button')].some((b) => b.textContent!.trim() === 'Validar mudança')).toBeFalse();
    expect(el.querySelector('[aria-label="Mais ações do levantamento"]')).toBeNull();
    // O status e a contagem de nós foram para a linha de metadados.
    expect(el.querySelector('.page-bar-meta .pill')!.textContent).toContain('Pronto');
    expect(el.querySelector('.page-bar-meta')!.textContent).toContain('3 passos');
  });

  // Abre o menu Exportar e devolve os itens.
  function openExport(el: HTMLElement): HTMLButtonElement[] {
    [...el.querySelectorAll<HTMLButtonElement>('.bar-group button')].find((b) => b.textContent!.trim() === 'Exportar')!.click();
    lastFixture!.detectChanges();
    return [...el.querySelectorAll<HTMLButtonElement>('#export-menu button')];
  }

  // WEB-UXC-208 (UX-FIM-208)
  it('menuExportar_noMapaPorSistemas_desabilitaImagemEMermaidEMantemODrawio', () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    graphOverride = graphWith(1, 0);
    const el = open(SurveyStatus.Completed);
    [...el.querySelectorAll<HTMLButtonElement>('.segmented button')][1].click();
    lastFixture!.detectChanges();

    const items = openExport(el);

    expect(items.slice(0, 4).map((i) => i.getAttribute('aria-disabled'))).toEqual(['true', 'true', 'true', 'true']);
    expect(items[4].hasAttribute('aria-disabled')).toBeFalse();
    const hint = items[0].getAttribute('aria-describedby')!;
    expect(el.querySelector(`#${hint}`)).not.toBeNull();
    items[0].click();
    lastFixture!.detectChanges();
    expect(el.querySelector('.menu-toast')).toBeNull();
    expect(el.querySelector('#export-menu')).not.toBeNull();
    localStorage.removeItem('rastro.layoutDirection');
  });

  // WEB-UXC-208 (UX-FIM-208)
  it('menuExportar_noConsolidado_desabilitaTambemODrawio', () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    graphOverride = graphWith(1, 0);
    const el = open(SurveyStatus.Completed);
    [...el.querySelectorAll<HTMLButtonElement>('.segmented button')][2].click();
    lastFixture!.detectChanges();

    const items = openExport(el);

    expect(items.slice(0, 5).every((i) => i.getAttribute('aria-disabled') === 'true')).toBeTrue();
    expect(items[5].hasAttribute('aria-disabled')).toBeFalse();
    localStorage.removeItem('rastro.layoutDirection');
  });

  // WEB-UXC-012, 152, 216 (UX-FIM-012, 152, 216)
  it('seletorEBotoesDaBarra_temDescricaoPopupEAnuncioDaTroca', () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    graphOverride = graphWith(1, 0);
    const el = open(SurveyStatus.Completed);

    const buttons = [...el.querySelectorAll<HTMLButtonElement>('.segmented button')];
    for (const button of buttons) {
      const id = button.getAttribute('aria-describedby')!;
      expect(el.querySelector(`#${id}`)!.textContent!.trim()).toBe(button.title);
    }
    const share = [...el.querySelectorAll<HTMLButtonElement>('.bar-group[aria-label="Compartilhar"] button')];
    expect(share.map((b) => b.getAttribute('aria-haspopup'))).toEqual(['menu', 'menu', 'dialog']);

    const live = el.querySelector('.segmented [aria-live="polite"]')!;
    expect(live.textContent!.trim()).toBe('');
    buttons[1].click();
    lastFixture!.detectChanges();
    expect(live.textContent!.trim()).not.toBe('');
    localStorage.removeItem('rastro.layoutDirection');
  });

  // WEB-UXC-203 (UX-FIM-203)
  it('menuExportar_comTab_fechaEDevolveOFocoAoBotao', () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    graphOverride = graphWith(1, 0);
    const el = open(SurveyStatus.Completed);
    const opener = [...el.querySelectorAll<HTMLButtonElement>('.bar-group button')].find((b) => b.textContent!.trim() === 'Exportar')!;
    opener.focus();
    openExport(el);

    el.querySelector('#export-menu')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
    lastFixture!.detectChanges();

    expect(el.querySelector('#export-menu')).toBeNull();
    expect(document.activeElement).toBe(opener);
    localStorage.removeItem('rastro.layoutDirection');
  });

  // WEB-UXC-151 (UX-FIM-151)
  it('baixarParaODrawio_semAsRotaDeLinks_avisaQueSaiuSemOsLinks', async () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    graphOverride = graphWith(1, 0);
    const el = open(SurveyStatus.Completed);
    await showBusiness(el);
    spyOn(URL, 'createObjectURL').and.returnValue('blob:teste');
    spyOn(HTMLAnchorElement.prototype, 'click');

    openExport(el).find((b) => b.textContent!.includes('draw.io'))!.click();
    TestBed.inject(HttpTestingController).match((r) => r.url.endsWith('/links')).forEach((r) => r.flush('erro', { status: 500, statusText: 'erro' }));
    lastFixture!.detectChanges();

    const toast = el.querySelector('.menu-toast')!;
    expect(toast.textContent).toContain('sem os links para o código');
    expect(toast.classList).not.toContain('is-error');
    localStorage.removeItem('rastro.layoutDirection');
  });

  // WEB-UXC-102 (UX-FIM-102)
  it('avisoDeErroDaExportacao_naoSomeSozinho_eFechaPeloBotao', () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    const el = open(SurveyStatus.Completed);
    jasmine.clock().install();
    try {
      const page = lastFixture!.componentInstance as unknown as { showExport(m: { ok: boolean; text: string }): void };
      page.showExport({ ok: false, text: 'Falhou de teste' });
      lastFixture!.detectChanges();
      jasmine.clock().tick(10000);
      lastFixture!.detectChanges();

      const toast = el.querySelector('.menu-toast')!;
      expect(toast.textContent).toContain('Falhou de teste');
      [...toast.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent!.includes('Fechar'))!.click();
      lastFixture!.detectChanges();
      expect(el.querySelector('.menu-toast')).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  // WEB-UXC-215 (UX-FIM-215)
  it('menuDoNo_guardaAVisaoPorLevantamentoEOAvisoDeSucessoTemAcaoVer', async () => {
    TestBed.resetTestingModule();
    localStorage.removeItem('rastro.layoutDirection');
    graphOverride = graphWith(3, 0);
    const el = open(SurveyStatus.Completed);
    const http = TestBed.inject(HttpTestingController);
    const page = lastFixture!.componentInstance as unknown as { openMenu(menu: { nodeId: string; x: number; y: number }): void };
    const businessRequests = () => http.match((r) => r.url.endsWith('/business'));

    page.openMenu({ nodeId: 'm1', x: 10, y: 10 });
    lastFixture!.detectChanges();
    const first = businessRequests();
    expect(first.length).toBe(1);
    first[0].flush(BUSINESS_FLOW);
    lastFixture!.detectChanges();

    // Segunda abertura (outro nó): usa o que já sabe, sem nova chamada.
    page.openMenu({ nodeId: 'm2', x: 10, y: 10 });
    lastFixture!.detectChanges();
    expect(businessRequests().length).toBe(0);
    const item = [...el.querySelectorAll<HTMLButtonElement>('.context-menu button')].find((b) => b.textContent!.includes('Mostrar na visão de negócio'));
    expect(item).toBeDefined();

    item!.click();
    http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/business/steps/m2/kind')).flush(BUSINESS_FLOW);
    lastFixture!.detectChanges();
    const see = [...el.querySelectorAll<HTMLButtonElement>('.menu-toast button')];
    expect(see.length).toBe(1);
    see[0].click();
    http.match(() => true).forEach((r) => r.flush(r.request.url.endsWith('/business') ? BUSINESS_FLOW : []));
    lastFixture!.detectChanges();

    expect(el.querySelector('app-business-view')).not.toBeNull();
    expect(el.querySelector('.menu-toast')).toBeNull();
    localStorage.removeItem('rastro.layoutDirection');
  });

});
