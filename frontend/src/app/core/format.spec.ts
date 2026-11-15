import { breakable, shortCommit } from './format';
import { tr } from './i18n/locale';

describe('format', () => {
  // WEB-I18N-021: em português o zero é plural ("0 passos"); o ICU `one` do CLDR o trataria como singular.
  it('o plural em ICU escolhe o singular só para 1', () => {
    expect(tr('survey.stepCount', { count: 1 })).toBe('1 passo');
    expect(tr('survey.stepCount', { count: 0 })).toBe('0 passos');
    expect(tr('survey.stepCount', { count: 58 })).toBe('58 passos');
  });

  it('shortCommit usa os 7 primeiros caracteres', () => {
    expect(shortCommit('ce7cec50abcdef')).toBe('ce7cec5');
    expect(shortCommit(undefined)).toBe('');
  });

  it('breakable permite quebra só depois de ponto e barra, sem mudar o texto visível', () => {
    const text = breakable('Loja.Notifications/Telegram.cs');

    expect(text.replace(/​/g, '')).toBe('Loja.Notifications/Telegram.cs');
    expect(text.split('​').length).toBe(4);
    expect(breakable(undefined)).toBe('');
  });
});
