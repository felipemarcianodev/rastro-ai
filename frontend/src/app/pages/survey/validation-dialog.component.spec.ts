import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CheckStatus, ValidationStatus, ValidationVerdict, ValidationView } from '../../core/models';
import { ValidationDialogComponent } from './validation-dialog.component';

function view(verdict: ValidationVerdict, extra: Partial<ValidationView> = {}): ValidationView {
  return {
    id: 'v1',
    surveyId: 's1',
    newSurveyId: 's2',
    status: ValidationStatus.Completed,
    createdAt: '2026-10-16T12:00:00Z',
    hasDocument: false,
    result: {
      expectations: [{ description: 'Job.Run chama Audit.Record', status: CheckStatus.Failed, detail: 'Audit.Record não aparece no fluxo levantado.' }],
      unexpected: [],
      decisionPointsBefore: 3,
      decisionPointsAfter: 4,
      build: { description: 'Build do repositório', status: CheckStatus.Passed },
      tests: { description: 'Testes do repositório', status: CheckStatus.Skipped, detail: 'Comando não configurado para este repositório.' },
      verdict,
      score: verdict === ValidationVerdict.Rejected ? 0 : 100,
    },
    ...extra,
  };
}

function render(validation: ValidationView): HTMLElement {
  TestBed.configureTestingModule({ imports: [ValidationDialogComponent], providers: [provideRouter([])] });
  const fixture = TestBed.createComponent(ValidationDialogComponent);
  fixture.componentRef.setInput('validation', validation);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('ValidationDialogComponent', () => {
  it('reprovada mostra a nota e o motivo, sem o botão de aprovar', () => {
    const el = render(view(ValidationVerdict.Rejected));

    expect(el.querySelector('.score-value')?.textContent?.trim()).toBe('0');
    expect(el.textContent).toContain('Reprovada');
    expect(el.textContent).toContain('Audit.Record não aparece no fluxo levantado.');
    expect(el.textContent).toContain('Pontos de decisão do fluxo: 3 → 4');
    expect(el.textContent).not.toContain('Aprovar e documentar');
  });

  it('aprovada oferece aprovar; com documento, oferece baixar', () => {
    expect(render(view(ValidationVerdict.Approved)).textContent).toContain('Aprovar e documentar');
    TestBed.resetTestingModule();

    const withDoc = render(view(ValidationVerdict.Approved, { hasDocument: true }));
    const link = withDoc.querySelector('a[download]') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/api/validations/v1/document');
  });

  it('em andamento avisa que atualiza sozinha', () => {
    const el = render(view(ValidationVerdict.Approved, { status: ValidationStatus.Running, result: undefined }));

    expect(el.textContent).toContain('Esta janela atualiza sozinha.');
    expect(el.textContent).not.toContain('Validar de novo');
    expect(el.textContent).toContain('Se fechar, a validação continua');
  });

  it('reprovada põe Validar de novo como primário e resume o motivo na linha da nota', () => {
    const el = render(view(ValidationVerdict.Rejected));

    expect(el.querySelector('.btn-p')?.textContent?.trim()).toBe('Validar de novo');
    expect(el.querySelector('.score-why')?.textContent).toBe('1 critério falhou.');
  });

  it('parcial deixa aprovar mesmo assim', () => {
    const el = render(view(ValidationVerdict.Partial));

    expect(el.querySelector('.btn-p')?.textContent?.trim()).toBe('Aprovar mesmo assim');
  });
});
