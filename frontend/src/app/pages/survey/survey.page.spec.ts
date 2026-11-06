import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CheckStatus, SourceLanguage, SurveyDetail, SurveyStatus, ValidationStatus, ValidationVerdict, ValidationView } from '../../core/models';
import { SurveyPage } from './survey.page';

const SURVEY_ID = 's1';

function detail(status: SurveyStatus): SurveyDetail {
  return {
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
      unresolvedCount: 0,
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
function open(status: SurveyStatus, validations: ValidationView[] = []): HTMLElement {
  TestBed.configureTestingModule({ imports: [SurveyPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
  const fixture = TestBed.createComponent(SurveyPage);
  fixture.componentRef.setInput('id', SURVEY_ID);
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
      expect(link!.getAttribute('href')).toBe('/repositorios/LojaBanco/banco');
      expect(link!.classList.contains('btn-small')).toBeTrue();
      expect(el.querySelector('.page-bar-meta a')).toBeNull();
    }
  });

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
});
