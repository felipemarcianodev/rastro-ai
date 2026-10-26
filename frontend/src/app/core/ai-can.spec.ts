import { AiProvider, AiStatus, AiUse, aiCan } from './models';

const status = (patch: Partial<AiStatus>): AiStatus => ({
  enabled: true,
  configured: true,
  provider: AiProvider.Claude,
  model: 'claude-opus-5-5',
  uses: [AiUse.FillHoles],
  tokensToday: 0,
  dailyTokenBudget: 0,
  ...patch,
});

// Sem IA ligada, configurada e com o uso habilitado, o botão de IA não aparece (sem botão morto).
describe('aiCan', () => {
  it('só libera com IA ligada, chave salva e o uso habilitado', () => {
    expect(aiCan(status({}), AiUse.FillHoles)).toBeTrue();
    expect(aiCan(status({ enabled: false }), AiUse.FillHoles)).toBeFalse();
    expect(aiCan(status({ configured: false }), AiUse.FillHoles)).toBeFalse();
    expect(aiCan(status({}), AiUse.DescribeRules)).toBeFalse();
    expect(aiCan(null, AiUse.FillHoles)).toBeFalse();
  });
});
