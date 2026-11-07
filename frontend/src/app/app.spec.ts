import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { App } from './app';
import { LastRepositoryService } from './core/last-repository.service';
import { RepositoryItem } from './core/models';

const repo = (name: string): RepositoryItem => ({ name, path: `C:\\codigo\\${name}`, hasGit: true, hasBuildCommand: false, hasTestCommand: false });

function render(repos: RepositoryItem[], status: object = {}) {
  const fixture = TestBed.createComponent(App);
  const http = TestBed.inject(HttpTestingController);
  http.match('/api/ai/status').forEach((r) => r.flush(status));
  http.expectOne('/api/repositories').flush(repos);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const link = (text: string) => Array.from(el.querySelectorAll<HTMLAnchorElement>('nav a')).find((a) => a.textContent?.trim() === text)!;
  return { fixture, el, link };
}

describe('App', () => {
  beforeEach(async () => {
    localStorage.removeItem('rastro.lastRepository');
    await TestBed.configureTestingModule({
      imports: [App],
      // Qualquer URL vale: o teste olha só o cabeçalho, não a tela da rota.
      providers: [provideRouter([{ path: '**', children: [] }]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('cria o shell da aplicação', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('mostra o nome do produto no topo', () => {
    const { el } = render([]);
    expect(el.querySelector('.brand-name')?.textContent).toContain('Rastro AI');
  });

  // UX-H01, UX-H05
  it('a nav tem os três itens e marca Levantamentos na tela inicial', async () => {
    const { fixture, el, link } = render([repo('a')]);
    await TestBed.inject(Router).navigateByUrl('/');
    fixture.detectChanges();
    const labels = Array.from(el.querySelectorAll('nav[aria-label="Principal"] a')).map((a) => a.textContent?.trim());
    expect(labels).toEqual(['Levantamentos', 'Banco', 'Configurações']);
    expect(link('Levantamentos').getAttribute('aria-current')).toBe('page');
    expect(link('Banco').getAttribute('aria-current')).toBeNull();
  });

  it('Levantamentos fica ativo no fluxo, Banco na tela do banco e Configurações nas configurações', async () => {
    const { fixture, link } = render([repo('a')]);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/surveys/5');
    fixture.detectChanges();
    expect(link('Levantamentos').getAttribute('aria-current')).toBe('page');
    await router.navigateByUrl('/repositorios/a/banco');
    fixture.detectChanges();
    expect(link('Banco').getAttribute('aria-current')).toBe('page');
    expect(link('Levantamentos').getAttribute('aria-current')).toBeNull();
    await router.navigateByUrl('/configuracoes#ia');
    fixture.detectChanges();
    expect(link('Configurações').getAttribute('aria-current')).toBe('page');
  });

  // UX-B01
  it('Banco abre o último repositório usado, se ainda cadastrado', () => {
    TestBed.inject(LastRepositoryService).name.set('b');
    const { link } = render([repo('a'), repo('b')]);
    expect(link('Banco').getAttribute('href')).toBe('/repositorios/b/banco');
  });

  it('Banco abre o único repositório', () => {
    TestBed.inject(LastRepositoryService).name.set('removido');
    const { link } = render([repo('unico')]);
    expect(link('Banco').getAttribute('href')).toBe('/repositorios/unico/banco');
  });

  it('Banco abre o primeiro quando o último não existe mais', () => {
    TestBed.inject(LastRepositoryService).name.set('removido');
    const { link } = render([repo('a'), repo('b')]);
    expect(link('Banco').getAttribute('href')).toBe('/repositorios/a/banco');
  });

  // UX-G-L12
  it('Banco sem repositório leva à tela inicial (primeiro uso)', () => {
    const { link } = render([]);
    expect(link('Banco').getAttribute('href')).toBe('/');
    expect(link('Banco').hasAttribute('aria-label')).toBeFalse();
  });

  // UX-G-L12
  it('erro ao ler os repositórios mantém a rota segura e avisa no link', () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);
    http.match('/api/ai/status').forEach((r) => r.flush({}));
    http.expectOne('/api/repositories').flush('falha', { status: 500, statusText: 'Erro' });
    fixture.detectChanges();
    const banco = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('nav a')).find((a) => a.textContent?.trim() === 'Banco')!;
    expect(banco.getAttribute('href')).toBe('/');
    expect(banco.getAttribute('aria-label')).toContain('não foi possível ler os repositórios');
  });

  // UX-H26
  it('o selo da IA diz "IA desligada" no primeiro uso e leva a #ia', () => {
    const { el } = render([], { enabled: false, configured: false });
    const badge = el.querySelector('a.ai-state') as HTMLAnchorElement;
    expect(badge.textContent?.trim()).toBe('IA desligada');
    expect(badge.getAttribute('aria-label')).toBe('IA desligada, abrir as configurações da IA');
    expect(badge.getAttribute('href')).toBe('/configuracoes#ia');
    expect(el.querySelector('a.gear')).toBeNull();
  });

  // UX-G-L10
  it('o selo da IA diz o estado: sem chave e com provedor', () => {
    const semChave = render([], { enabled: true, configured: false, provider: 0 });
    const badge = semChave.el.querySelector('a.ai-state') as HTMLAnchorElement;
    expect(badge.textContent?.trim()).toBe('IA sem chave');
    expect(badge.getAttribute('aria-label')).toBe('IA sem chave, abrir as configurações da IA');
  });

  // UX-G-L10
  it('o selo da IA mostra o provedor quando a IA está pronta', () => {
    const { el } = render([], { enabled: true, configured: true, provider: 0 });
    const badge = el.querySelector('a.ai-state') as HTMLAnchorElement;
    expect(badge.textContent?.trim()).toBe('IA: Claude (Anthropic)');
    expect(badge.getAttribute('aria-label')).toBe('IA: Claude (Anthropic), abrir as configurações da IA');
  });

  // UX-H04
  it('o cabeçalho não tem mais paleta nem tema', () => {
    const { el } = render([]);
    expect(el.querySelector('select')).toBeNull();
    expect(el.querySelector('header button')).toBeNull();
  });

  // UX-FIM-C03
  it('tem o link "Ir para o conteúdo" que foca o main', () => {
    const { el } = render([]);
    const skip = el.querySelector<HTMLAnchorElement>('a.skip-link')!;
    const main = el.querySelector<HTMLElement>('main#conteudo')!;
    expect(skip.textContent?.trim()).toBe('Ir para o conteúdo');
    expect(main.getAttribute('tabindex')).toBe('-1');
    document.body.appendChild(el);
    skip.click();
    expect(document.activeElement).toBe(main);
    el.remove();
  });
});

@Component({ template: '<h1>Pagina A</h1><button id="item" type="button">Item</button>' })
class PageAComponent {}

@Component({ template: '<h1>Pagina B</h1><button id="item" type="button">Item</button>' })
class PageBComponent {
  constructor() {
    // Como ?passo= e ?tabela=: a página já pôs o foco num item ao abrir.
    setTimeout(() => document.getElementById('item')?.focus());
  }
}

@Component({ template: '<h1>Pagina C</h1>' })
class PageCComponent {}

// UX-FIM-C12
describe('App, foco no h1 depois da navegação', () => {
  const tick = () => new Promise<void>((resolve) => setTimeout(resolve));

  async function setup() {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([
          { path: 'a', component: PageAComponent },
          { path: 'b', component: PageBComponent },
          { path: 'c', component: PageCComponent },
          { path: '**', children: [] },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);
    http.match('/api/ai/status').forEach((r) => r.flush({}));
    http.expectOne('/api/repositories').flush([]);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    return { fixture, router: TestBed.inject(Router) };
  }

  it('foca o h1 da página nova, com tabindex -1 posto pelo código, e não na primeira carga', async () => {
    const { fixture, router } = await setup();
    await router.navigateByUrl('/a');
    fixture.detectChanges();
    await tick();
    expect(document.activeElement?.tagName).toBe('BODY');

    await router.navigateByUrl('/c');
    fixture.detectChanges();
    await tick();
    const h1 = document.activeElement as HTMLElement;
    expect(h1.tagName).toBe('H1');
    expect(h1.textContent).toBe('Pagina C');
    expect(h1.getAttribute('tabindex')).toBe('-1');
    fixture.nativeElement.remove();
  });

  it('não tira o foco de um item que a página já selecionou', async () => {
    const { fixture, router } = await setup();
    await router.navigateByUrl('/a');
    fixture.detectChanges();
    await router.navigateByUrl('/b');
    fixture.detectChanges();
    await tick();
    await tick();
    expect(document.activeElement?.id).toBe('item');
    fixture.nativeElement.remove();
  });

  it('mudança só de query ou fragmento na mesma página não move o foco', async () => {
    const { fixture, router } = await setup();
    await router.navigateByUrl('/a');
    fixture.detectChanges();
    await router.navigateByUrl('/c');
    fixture.detectChanges();
    await tick();
    const item = document.createElement('button');
    fixture.nativeElement.querySelector('main').appendChild(item);
    item.focus();
    await router.navigateByUrl('/c?aba=2');
    fixture.detectChanges();
    await tick();
    expect(document.activeElement).toBe(item);
    fixture.nativeElement.remove();
  });
});
