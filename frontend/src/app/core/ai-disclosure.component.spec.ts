import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AiDisclosureComponent } from './ai-disclosure.component';
import { AiStatusService } from './ai-status.service';
import { AiProvider, AiStatus, AiUse } from './models';

// UX-FIM-Q06
describe('AiDisclosureComponent', () => {
  const status = (patch: Partial<AiStatus> = {}): AiStatus => ({
    enabled: true, configured: true, provider: AiProvider.Claude, model: 'm', uses: [AiUse.Suggestions], tokensToday: 1200, dailyTokenBudget: 50000, ...patch,
  });

  function setup(value: AiStatus | null) {
    TestBed.configureTestingModule({ imports: [AiDisclosureComponent], providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(AiStatusService).status.set(value);
    const fixture = TestBed.createComponent(AiDisclosureComponent);
    fixture.componentRef.setInput('goes', 'o fluxo');
    fixture.componentRef.setInput('never', 'Nada mais vai.');
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('mostra o provedor, o que vai, o que nunca vai e os tokens de hoje', () => {
    const text = setup(status()).textContent ?? '';

    expect(text).toContain('Claude');
    expect(text).toContain('o fluxo');
    expect(text).toContain('Nada mais vai.');
    expect(text).toContain('gasta tokens');
    expect(text).toMatch(/Hoje: 1\.200 de 50\.000 tokens/);
  });

  it('sem limite diário, diz que não há limite', () => {
    expect(setup(status({ dailyTokenBudget: 0 })).textContent).toContain('(sem limite)');
  });

  it('sem IA ligada ou sem chave, não mostra nada', () => {
    expect(setup(status({ enabled: false })).textContent?.trim()).toBe('');
  });
});
