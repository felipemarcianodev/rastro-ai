import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('cria o shell da aplicação', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('mostra o nome do produto no topo', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.brand-name')?.textContent).toContain('Rastro AI');
  });

  it('a engrenagem do cabeçalho leva às configurações', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const gear = (fixture.nativeElement as HTMLElement).querySelector('a.gear') as HTMLAnchorElement;
    expect(gear.textContent?.trim()).toBe('Configurações');
    expect(gear.getAttribute('title')).toBe('Configurações');
    expect(gear.getAttribute('href')).toBe('/configuracoes');
  });

  // UX-H05
  it('a marca marca a tela inicial como página atual', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    // A primeira navegação para "/" é para a mesma URL e não conta como navegada; o RouterLinkActive só
    // marca depois de uma navegação concluída.
    await router.navigateByUrl('/', { onSameUrlNavigation: 'reload' });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const brand = (fixture.nativeElement as HTMLElement).querySelector('a.brand') as HTMLAnchorElement;
    expect(brand.getAttribute('aria-current')).toBe('page');
  });
});
