import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CheckStatus, EdgeConfidence, FlowGraph, NodeKind, SourceLanguage, SurveyDetail, SurveyStatus, ValidationStatus, ValidationVerdict, ValidationView } from '../../core/models';
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

describe('SurveyPage', () => {
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
  it('o seletor tem as três visões, com a atual marcada e a troca pelo clique', () => {
    TestBed.resetTestingModule();
    const el = open(SurveyStatus.Completed);

    const group = el.querySelector('.segmented')!;
    expect(group.getAttribute('role')).toBe('group');
    const items = [...group.querySelectorAll<HTMLButtonElement>('button')];
    expect(items.map((b) => b.textContent!.trim())).toEqual(['Grafo', 'Mapa por sistemas', 'Visão consolidada']);
    expect(items.map((b) => b.getAttribute('aria-pressed'))).toEqual(['true', 'false', 'false']);

    items[2].click();
    lastFixture!.detectChanges();
    const after = [...el.querySelectorAll<HTMLButtonElement>('.segmented button')];
    expect(after.map((b) => b.getAttribute('aria-pressed'))).toEqual(['false', 'false', 'true']);
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
    expect(share.map((b) => b.textContent!.trim())).toEqual(['Comparar', 'Exportar']);
    expect(share[1].title).toBe('PNG, SVG, Mermaid e C4');
    expect(el.querySelector('.bar-group[aria-label="Alterar"] .btn-p')!.textContent!.trim()).toBe('Editar fluxo');
    const remove = [...el.querySelectorAll<HTMLButtonElement>('.page-bar-side button')].find((b) => b.textContent!.trim() === 'Excluir levantamento…');
    expect(remove).toBeUndefined();
    expect([...el.querySelectorAll('.page-bar-side button')].some((b) => b.textContent!.trim() === 'Validar mudança')).toBeFalse();
    expect(el.querySelector('[aria-label="Mais ações do levantamento"]')).toBeNull();
    // O status e a contagem de nós foram para a linha de metadados.
    expect(el.querySelector('.page-bar-meta .pill')!.textContent).toContain('Pronto');
    expect(el.querySelector('.page-bar-meta')!.textContent).toContain('3 passos');
  });

});
