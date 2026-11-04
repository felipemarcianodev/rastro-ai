import { Component, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AiStatusService } from '../../core/ai-status.service';
import { MAP_FIXTURE, MAP_FIXTURE_GRAPH, QUESTIONS_FIXTURE, RULES_FIXTURE } from '../../core/graph/flow-map-fixture.spec';
import { AiProvider, AiStatus, AiUse, AnswerSeal, SourceLanguage, SurveyDetail, SurveyStatus } from '../../core/models';
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

const aiStatus = (uses: AiUse[]): AiStatus => ({ enabled: true, configured: true, provider: AiProvider.Claude, model: 'm', uses, tokensToday: 0, dailyTokenBudget: 0 });

@Component({
  imports: [FlowMapViewComponent],
  template: `<app-flow-map-view [detail]="detail" [selectedId]="selected()" (selectNode)="picked.push($event)" />`,
})
class HostComponent {
  readonly detail = detail;
  readonly selected = signal<string | null>(null);
  readonly picked: (string | null)[] = [];
}

async function setup(uses: AiUse[] | null = null) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideHttpClient(), provideHttpClientTesting()] });
  if (uses) TestBed.inject(AiStatusService).status.set(aiStatus(uses));
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  const http = TestBed.inject(HttpTestingController);
  http.expectOne('/api/surveys/s1/map').flush(MAP_FIXTURE);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const clickTab = (name: string) => Array.from(el.querySelectorAll<HTMLButtonElement>('[role="tab"]')).find((b) => b.textContent!.trim() === name)!.click();
  const openTab = async (name: string, url: string, body: object) => {
    clickTab(name);
    fixture.detectChanges();
    http.expectOne(url).flush(body);
    await settle();
  };
  const text = (selector: string) => Array.from(el.querySelectorAll(selector)).map((n) => n.textContent!.replace(/\s+/g, ' ').trim());
  const button = (scope: string, label: string) => Array.from(el.querySelectorAll<HTMLButtonElement>(`${scope} button`)).find((b) => b.textContent!.includes(label))!;
  const ask = async (question: string) => {
    const input = el.querySelector<HTMLInputElement>('#map-ask')!;
    input.value = question;
    input.dispatchEvent(new Event('input'));
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  };
  return { fixture, el, http, host: fixture.componentInstance, settle, clickTab, openTab, text, button, ask };
}

describe('painel do mapa: regras (Fase 32)', () => {
  // WEB-INS-010
  it('regras_filtroPorEfeitoEMarcasDeEscritaPorVoceEDuplicada', async () => {
    const { el, openTab, button, settle, text } = await setup();
    await openTab('Regras', '/api/surveys/s1/rules', RULES_FIXTURE);

    expect(el.querySelectorAll('.rule').length).toBe(3);
    expect(text('.rule-mark')).toEqual(['implementada em 2 sistemas', 'escrita por você']);
    button('app-flow-map-rules .insight-filters', 'Bloqueia').click();
    await settle();
    expect(text('.rule-text')).toEqual(['Bloqueia quando o pagamento não foi aprovado']);
    expect(el.querySelector('.rule-blocks .rule-glyph')!.textContent).toBe('B');
    button('app-flow-map-rules .insight-filters', 'Todas').click();
    await settle();
    expect(el.querySelectorAll('.rule').length).toBe(3);
  });

  // WEB-INS-011
  it('regras_aFonteClicavelEscolheOBlocoNoMapa', async () => {
    const { el, host, openTab, settle } = await setup();
    await openTab('Regras', '/api/surveys/s1/rules', RULES_FIXTURE);

    const sources = Array.from(el.querySelectorAll<HTMLButtonElement>('.rule .source-link'));
    expect(sources[1].textContent).toContain('api-pagamentos Estornos.cs:9');
    sources[1].click();
    await settle();

    expect(host.picked).toEqual(['n5']);
    expect(el.querySelector('[data-block-id="b4"]')!.classList).toContain('is-selected');
  });

  // WEB-INS-012
  it('regras_erroDaApiMostraMensagem', async () => {
    const { el, http, fixture, settle, clickTab } = await setup();
    clickTab('Regras');
    fixture.detectChanges();
    http.expectOne('/api/surveys/s1/rules').flush({ code: 'NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    await settle();

    expect(el.querySelector('app-flow-map-rules [role="alert"]')!.textContent).toContain('Não encontrado.');
  });
});

describe('painel do mapa: perguntas (Fase 33)', () => {
  // WEB-INS-020
  it('perguntas_filtroPorPapel', async () => {
    const { el, openTab, button, settle, text } = await setup();
    await openTab('Perguntas', '/api/surveys/s1/questions', QUESTIONS_FIXTURE);

    expect(el.querySelectorAll('.question').length).toBe(2);
    button('app-flow-map-questions .insight-filters', 'Gerente').click();
    await settle();
    expect(text('.question')).toEqual(['GerenteO que acontece se a chamada ao estorno falhar?']);
    button('app-flow-map-questions .insight-filters', 'TL').click();
    await settle();
    expect(text('.question')).toEqual(['TLQuando o pedido é recusado?']);
  });

  // WEB-INS-021
  it('perguntas_respostaComSeloTextoComQuebraGapEFonteClicavel', async () => {
    const { el, host, openTab, button, settle } = await setup();
    await openTab('Perguntas', '/api/surveys/s1/questions', QUESTIONS_FIXTURE);

    button('app-flow-map-questions .question-list', 'estorno falhar').click();
    await settle();
    expect(el.querySelector('.seal')!.textContent).toContain('Fora do código levantado');
    expect(el.querySelector('.seal-glyph')!.textContent).toBe('↗');
    expect(el.querySelector('.answer-gap')!.textContent).toContain('não trata a falha');
    expect(el.querySelector('.answer-provider')).toBeNull();

    el.querySelector<HTMLButtonElement>('.answer .source-link')!.click();
    await settle();
    expect(host.picked).toEqual(['n4']);
    expect(el.querySelector('[data-block-id="b3"]')!.classList).toContain('is-selected');

    button('app-flow-map-questions .question-list', 'Quando o pedido').click();
    await settle();
    expect(el.querySelector('.answer-text')!.textContent).toContain('\n');
    expect(el.querySelector('.answer-gap')).toBeNull();
    expect(el.querySelector('.seal')!.textContent).toContain('Extraída do código');
  });

  // WEB-INS-022
  it('perguntaLivre_escondidaSemIAEUsoSugestoes', async () => {
    const without = await setup();
    await without.openTab('Perguntas', '/api/surveys/s1/questions', QUESTIONS_FIXTURE);
    expect(without.el.querySelector('.ask')).toBeNull();

    const otherUse = await setup([AiUse.FillHoles]);
    await otherUse.openTab('Perguntas', '/api/surveys/s1/questions', QUESTIONS_FIXTURE);
    expect(otherUse.el.querySelector('.ask')).toBeNull();

    const withAi = await setup([AiUse.Suggestions]);
    await withAi.openTab('Perguntas', '/api/surveys/s1/questions', QUESTIONS_FIXTURE);
    expect(withAi.el.querySelector('.ask')).not.toBeNull();
  });

  // WEB-INS-023
  it('perguntaLivre_enviaMostraEnviandoERespostaMarcadaComoDaIA', async () => {
    const { el, http, openTab, settle, ask } = await setup([AiUse.Suggestions]);
    await openTab('Perguntas', '/api/surveys/s1/questions', QUESTIONS_FIXTURE);

    await ask('O que acontece se o pagamento for recusado?');
    expect(el.querySelector<HTMLButtonElement>('.ask button[type="submit"]')!.textContent).toContain('Perguntando');
    expect(el.querySelector<HTMLInputElement>('#map-ask')!.disabled).toBeTrue();

    const request = http.expectOne('/api/surveys/s1/ai/ask');
    expect(request.request.body).toEqual({ question: 'O que acontece se o pagamento for recusado?' });
    request.flush({ answer: { text: 'O pedido é recusado.', seal: AnswerSeal.Inferred, sources: ['n3'], gap: null }, provider: AiProvider.Claude });
    await settle();

    expect(el.querySelector('.seal')!.textContent).toContain('Inferida pela IA');
    expect(el.querySelector('.answer-provider')!.textContent).toContain('Claude');
    expect(el.querySelector('.answer-text')!.textContent).toBe('O pedido é recusado.');
  });

  // WEB-INS-024
  it('perguntaLivre_erroPeloCodigoDaApi', async () => {
    const { el, http, openTab, settle, ask } = await setup([AiUse.Suggestions]);
    await openTab('Perguntas', '/api/surveys/s1/questions', QUESTIONS_FIXTURE);

    await ask('Pergunta');
    http.expectOne('/api/surveys/s1/ai/ask').flush({ code: 'AI_BUDGET_EXCEEDED' }, { status: 429, statusText: 'Too Many Requests' });
    await settle();

    expect(el.querySelector('.ask [role="alert"]')!.textContent).toContain('orçamento diário');
    expect(el.querySelector<HTMLInputElement>('#map-ask')!.disabled).toBeFalse();
    expect(el.querySelector('.answer')).toBeNull();
  });
});

describe('painel do mapa: campos da chamada (Fase 34)', () => {
  // WEB-INS-030
  it('chamada_mostraOsCamposEEOutrosCamposNoPainelENaDicaDaLigacao', async () => {
    const { el, settle } = await setup();

    el.querySelector<HTMLButtonElement>('[data-block-id="b3"]')!.click();
    await settle();
    expect(el.querySelector('.map-panel .map-fields')!.textContent).toContain('campos: pedidoId, valor e outros campos');
    const label = Array.from(el.querySelectorAll('.map-label')).find((l) => l.textContent!.includes('POST api/estornos'))!;
    expect(label.getAttribute('title')).toBe('POST api/estornos. Campos: pedidoId, valor e outros campos');
  });
});
