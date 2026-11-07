import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { C4DialogComponent } from './c4-dialog.component';
import { AiStatusService } from '../../core/ai-status.service';
import { AiProvider, AiUse } from '../../core/models';

describe('C4DialogComponent', () => {
  function setup() {
    TestBed.configureTestingModule({ imports: [C4DialogComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(C4DialogComponent);
    fixture.componentRef.setInput('surveyId', 's1');
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys/s1/c4').flush({ dsl: 'workspace "x" {}', people: [{ name: 'Operador', description: 'Acompanha' }] });
    fixture.detectChanges();
    return { fixture, http, el: fixture.nativeElement as HTMLElement };
  }

  it('mostra o DSL, as pessoas salvas e o link de download', async () => {
    const { fixture, el } = setup();
    await fixture.whenStable();

    expect(el.querySelector('.dsl')?.textContent).toContain('workspace "x"');
    expect((el.querySelector('#c4-name-0') as HTMLInputElement).value).toBe('Operador');
    expect(el.querySelector('a[download]')?.getAttribute('href')).toBe('/api/surveys/s1/c4/workspace.dsl');
  });

  // UX-G-F16
  it('o texto do diálogo não cita nome de ferramenta do MCP', () => {
    const { el } = setup();
    const head = el.querySelector('.dialog-head')!.textContent!;
    expect(head).toContain('Um agente conectado ao Rastro (MCP) obtém o mesmo resultado.');
    expect(head).not.toContain('get_c4');
  });

  // UX-G-F17
  it('mostra o comando do Structurizr com %CD% e com $PWD', () => {
    const { el } = setup();
    const codes = Array.from(el.querySelectorAll('.hint code')).map((c) => c.textContent);
    expect(codes.some((c) => c?.includes('%CD%'))).toBeTrue();
    expect(codes.some((c) => c?.includes('$PWD'))).toBeTrue();
  });

  // UX-G-F17
  it('com pessoas não salvas, o download fica com aria-disabled, sem href e não baixa', () => {
    const { fixture, el } = setup();
    (el.querySelector('.people-actions button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const name = el.querySelector('#c4-name-1') as HTMLInputElement;
    name.value = 'Nova';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const link = el.querySelector('a[download]') as HTMLAnchorElement;
    expect(link.getAttribute('aria-disabled')).toBe('true');
    expect(link.hasAttribute('href')).toBeFalse();
    const click = new MouseEvent('click', { cancelable: true, bubbles: true });
    link.dispatchEvent(click);
    expect(click.defaultPrevented).toBeTrue();
  });

  it('salvar ignora linha vazia e mostra o DSL novo', () => {
    const { fixture, http, el } = setup();

    (el.querySelector('.people-actions button') as HTMLButtonElement).click();
    fixture.detectChanges();
    (el.querySelectorAll('.people-actions button')[1] as HTMLButtonElement).click();

    const request = http.expectOne('/api/surveys/s1/c4/people');
    expect(request.request.body).toEqual([{ name: 'Operador', description: 'Acompanha' }]);
    request.flush({ dsl: 'workspace "novo" {}', people: [{ name: 'Operador', description: 'Acompanha' }] });
    fixture.detectChanges();
    expect(el.querySelector('.dsl')?.textContent).toContain('workspace "novo"');
    expect(el.textContent).toContain('Pessoas salvas.');
  });

  it('pessoa só com descrição não salva e marca o campo do nome', () => {
    const { fixture, http, el } = setup();

    (el.querySelector('.people-actions button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const description = el.querySelector('#c4-desc-1') as HTMLInputElement;
    description.value = 'Só descrição';
    description.dispatchEvent(new Event('input'));
    (el.querySelectorAll('.people-actions button')[1] as HTMLButtonElement).click();
    fixture.detectChanges();

    http.expectNone('/api/surveys/s1/c4/people');
    expect(el.querySelector('#c4-name-1')?.getAttribute('aria-invalid')).toBe('true');
    expect(el.textContent).toContain('Dê um nome a esta pessoa.');
  });

  it('erro ao carregar oferece tentar de novo', () => {
    TestBed.configureTestingModule({ imports: [C4DialogComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(C4DialogComponent);
    fixture.componentRef.setInput('surveyId', 's1');
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys/s1/c4').flush({ code: 'X', message: 'Falhou' }, { status: 500, statusText: 'Erro' });
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).not.toContain('Montando os diagramas');
    (el.querySelector('.notice button') as HTMLButtonElement).click();
    http.expectOne('/api/surveys/s1/c4').flush({ dsl: 'workspace "y" {}', people: [] });
    fixture.detectChanges();
    expect(el.querySelector('.dsl')?.textContent).toContain('workspace "y"');
    expect(el.textContent).toContain('Ninguém cadastrado ainda.');
  });

  // UX-FIM-Q06
  it('o aviso do que vai ao provedor e dos tokens só aparece com a IA de contexto ligada', () => {
    const { fixture, el } = setup();
    // UX-G-F23: o aviso mora ao lado do botão de IA, que só existe com a IA de contexto ligada.
    expect(el.querySelector('app-ai-disclosure')).toBeNull();

    TestBed.inject(AiStatusService).status.set({
      enabled: true, configured: true, provider: AiProvider.Claude, model: 'm', uses: [AiUse.ContextSuggestions], tokensToday: 1500, dailyTokenBudget: 10000,
    });
    fixture.detectChanges();

    const text = el.querySelector('app-ai-disclosure')?.textContent ?? '';
    expect(text).toContain('Vai para Claude');
    expect(text).toContain('as classes do fluxo');
    expect(text).toContain('Hoje: 1.500 de 10.000 tokens.');
  });

  // UX-G-F23
  it('o aviso da IA vem logo depois do botão de sugerir pessoas', () => {
    const { fixture, el } = setup();
    TestBed.inject(AiStatusService).status.set({
      enabled: true, configured: true, provider: AiProvider.Claude, model: 'm', uses: [AiUse.ContextSuggestions], tokensToday: 0, dailyTokenBudget: 0,
    });
    fixture.detectChanges();

    const ask = Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Sugerir pessoas com a IA'));
    expect(ask?.nextElementSibling?.tagName.toLowerCase()).toBe('app-ai-disclosure');
  });

  it('com a IA de contexto ligada, sugere pessoas e acrescenta só a escolhida', async () => {
    const { fixture, http, el } = setup();
    TestBed.inject(AiStatusService).status.set({
      enabled: true, configured: true, provider: AiProvider.Claude, model: 'm', uses: [AiUse.ContextSuggestions], tokensToday: 0, dailyTokenBudget: 0,
    });
    fixture.detectChanges();

    const ask = Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Sugerir pessoas com a IA')) as HTMLButtonElement;
    ask.click();
    http.expectOne('/api/surveys/s1/c4/ai/people').flush({ people: [{ name: 'Cliente', description: 'Compra' }], provider: AiProvider.Claude });
    fixture.detectChanges();
    expect(el.querySelector('.ai-suggestion')?.textContent).toContain('Cliente');

    (Array.from(el.querySelectorAll('.ai-suggestion button')).find((b) => b.textContent?.includes('Acrescentar')) as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect((el.querySelector('#c4-name-1') as HTMLInputElement).value).toBe('Cliente');
    expect(el.textContent).toContain('Pessoas alteradas e não salvas');
    expect(el.querySelectorAll('.person-origin').length).toBe(1);
  });

  it('pessoa sugerida pela IA mantém o selo ao salvar e perde ao ser editada', async () => {
    TestBed.configureTestingModule({ imports: [C4DialogComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(C4DialogComponent);
    fixture.componentRef.setInput('surveyId', 's1');
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const people = [{ name: 'Cliente', description: 'Compra', fromAi: true }, { name: 'Gestor' }];
    http.expectOne('/api/surveys/s1/c4').flush({ dsl: 'x', people });
    fixture.detectChanges();
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const save = () => (Array.from(el.querySelectorAll('.people-actions button')).find((b) => b.textContent?.includes('Salvar')) as HTMLButtonElement).click();
    expect(el.querySelectorAll('.person-origin').length).toBe(1);

    save();
    const first = http.expectOne('/api/surveys/s1/c4/people');
    expect(first.request.body).toEqual([{ name: 'Cliente', description: 'Compra', fromAi: true }, { name: 'Gestor', description: undefined }]);
    first.flush({ dsl: 'x', people });
    fixture.detectChanges();
    await fixture.whenStable();

    const name = el.querySelector('#c4-name-0') as HTMLInputElement;
    name.value = 'Cliente final';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(el.querySelectorAll('.person-origin').length).toBe(0);
    save();
    expect(http.expectOne('/api/surveys/s1/c4/people').request.body[0]).toEqual({ name: 'Cliente final', description: 'Compra' });
  });
});
