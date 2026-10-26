import { breakable, plural, shortCommit } from './format';

describe('format', () => {
  it('plural escolhe singular só para 1', () => {
    expect(plural(1, 'nó', 'nós')).toBe('1 nó');
    expect(plural(0, 'nó', 'nós')).toBe('0 nós');
    expect(plural(58, 'nó', 'nós')).toBe('58 nós');
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
