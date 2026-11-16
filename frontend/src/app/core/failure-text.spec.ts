import { HttpErrorResponse } from '@angular/common/http';
import { draftErrorText, refusalText, surveyFailureText, validationFailureText } from './failure-text';
import { registerStaticTranslator } from './i18n/i18n.testing';
import { UiLanguage } from './i18n/language';
import { DraftError, ProposalRefusal, SurveyFailure, ValidationFailure } from './models';
import { apiErrorMessage } from './rastro-api.service';

// Valores numéricos de um enum TS (sem as chaves reversas).
function values(enumObject: object): number[] {
  return Object.values(enumObject).filter((v): v is number => typeof v === 'number');
}

describe('failure-text', () => {
  afterEach(() => registerStaticTranslator(UiLanguage.Portuguese));

  it('WEB-I18N-101: falha do levantamento com código traduz no idioma ativo, com os parâmetros', () => {
    const args = ['AlertJob.NaoExiste', 'NaoExiste'];

    registerStaticTranslator(UiLanguage.Portuguese);
    expect(surveyFailureText(SurveyFailure.EntryNotFound, args)).toContain('nenhum método chamado NaoExiste');
    registerStaticTranslator(UiLanguage.English);
    expect(surveyFailureText(SurveyFailure.EntryNotFound, args)).toBe('Method "AlertJob.NaoExiste" was not found in the solution: no method named NaoExiste with a body exists in it.');
    registerStaticTranslator(UiLanguage.Spanish);
    expect(surveyFailureText(SurveyFailure.EntryNotFound, args)).toContain('ningún método llamado NaoExiste');
  });

  it('WEB-I18N-102: sem código (falha antiga) mostra o texto antigo gravado', () => {
    expect(surveyFailureText(null, [], 'Texto antigo em português.')).toBe('Texto antigo em português.');
    expect(surveyFailureText(undefined, undefined, 'Texto antigo em português.')).toBe('Texto antigo em português.');
    expect(validationFailureText(null, [], 'Validação antiga.')).toBe('Validação antiga.');
    expect(refusalText(null, [], 'Recusa antiga.')).toBe('Recusa antiga.');
    expect(draftErrorText(null, [], 'Erro antigo.')).toBe('Erro antigo.');
  });

  it('WEB-I18N-103: código que esta versão da tela não conhece cai no texto antigo', () => {
    expect(surveyFailureText(999, [], 'Do servidor mais novo.')).toBe('Do servidor mais novo.');
    expect(surveyFailureText(999, [])).toBe('');
  });

  it('WEB-I18N-104: falha da validação com a causa da análise aninhada traduz a causa', () => {
    const nested = [String(SurveyFailure.RestoreFailed)];

    registerStaticTranslator(UiLanguage.English);
    expect(validationFailureText(ValidationFailure.Analysis, nested)).toBe('dotnet restore failed for the solution. Check that it builds on this machine.');
    expect(validationFailureText(ValidationFailure.EntryGone, ['Job.Run'])).toBe('The entry point "Job.Run" no longer exists in the code.');
  });

  it('WEB-I18N-105: recusa de proposta traduz com os parâmetros e a causa aninhada', () => {
    registerStaticTranslator(UiLanguage.Portuguese);
    expect(refusalText(ProposalRefusal.RuleLength, ['10', '2000'])).toBe('A regra precisa ter de 10 a 2000 caracteres.');
    expect(refusalText(ProposalRefusal.AnalysisFailed, [String(SurveyFailure.NodeMissing)])).toContain('O Node não foi encontrado');
    expect(refusalText(ProposalRefusal.ProofTargetInvalid, [String(SurveyFailure.EntryInvalid), 'x'])).toBe(
      'O método de destino não confere: Ponto de entrada inválido: "x". Use Tipo.Metodo ou Namespace.Tipo.Metodo.',
    );
  });

  it('WEB-I18N-106: erros do rascunho vêm do código na lista issues da API', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: {
        code: 'DRAFT_INVALID',
        errors: ['texto antigo 1', 'texto antigo 2'],
        issues: [
          { code: DraftError.CodeStepMissing, args: ['Job.A'] },
          { code: DraftError.ForeignSystem, args: [] },
        ],
      },
    });

    registerStaticTranslator(UiLanguage.English);
    expect(apiErrorMessage(error)).toBe(
      'The code step "Job.A" is missing from the draft. To remove it, mark it as removed. Editing is allowed only in the first system of the flow.',
    );
  });

  it('WEB-I18N-107: resposta antiga, só com errors, mostra o texto', () => {
    const error = new HttpErrorResponse({ status: 400, error: { code: 'DRAFT_INVALID', errors: ['um erro', 'outro erro'] } });

    expect(apiErrorMessage(error)).toBe('um erro outro erro');
  });

  it('WEB-I18N-108: todo código tem texto nos três idiomas', () => {
    for (const language of [UiLanguage.Portuguese, UiLanguage.English, UiLanguage.Spanish]) {
      registerStaticTranslator(language);
      const many = ['a', 'b', 'c', 'd', 'e'];
      const nested = [String(SurveyFailure.RestoreFailed)];
      for (const code of values(SurveyFailure)) expect(surveyFailureText(code, many)).not.toMatch(/^failure\./);
      for (const code of values(ValidationFailure)) expect(validationFailureText(code, code === ValidationFailure.Analysis ? nested : many)).not.toMatch(/^failure\./);
      for (const code of values(ProposalRefusal)) {
        const nests = code === ProposalRefusal.AnalysisFailed || code === ProposalRefusal.ProofTargetInvalid;
        expect(refusalText(code, nests ? nested : many)).not.toMatch(/^refusal\./);
      }
      for (const code of values(DraftError)) expect(draftErrorText(code, many)).not.toMatch(/^draftError\./);
    }
  });

  it('WEB-I18N-109: os enums espelham os valores do servidor', () => {
    expect(SurveyFailure.RestoreFailed).toBe(13);
    expect(ValidationFailure.Analysis).toBe(6);
    expect(ProposalRefusal.RouteMismatch).toBe(21);
    expect(DraftError.ForeignSystem).toBe(15);
  });
});
