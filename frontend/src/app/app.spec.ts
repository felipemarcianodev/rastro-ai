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

  it('Banco sem repositório leva às configurações, no editor', () => {
    const { link } = render([]);
    expect(link('Banco').getAttribute('href')).toBe('/configuracoes#editor');
  });

  // UX-H26
  it('o selo da IA diz "IA: configurar" no primeiro uso e leva a #ia', () => {
    const { el } = render([], { enabled: false, configured: false });
    const badge = el.querySelector('a.ai-state') as HTMLAnchorElement;
    expect(badge.textContent?.trim()).toBe('IA: configurar');
    expect(badge.getAttribute('href')).toBe('/configuracoes#ia');
    expect(el.querySelector('a.gear')).toBeNull();
  });

  // UX-H04
  it('o cabeçalho não tem mais paleta nem tema', () => {
    const { el } = render([]);
    expect(el.querySelector('select')).toBeNull();
    expect(el.querySelector('header button')).toBeNull();
  });
});
