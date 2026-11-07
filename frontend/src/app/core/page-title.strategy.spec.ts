import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter } from '@angular/router';
import { PageTitleStrategy, SurveyPageTitle, resolveTitle, surveyTitle } from './page-title.strategy';

describe('PageTitleStrategy', () => {
  // UX-FIM-C11
  it('troca :param pelo valor da rota e mantém título fixo sem parâmetro', async () => {
    const page = class {};
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'repositorios/:nome/banco', title: 'Banco de :nome | Rastro AI', component: page },
          { path: 'configuracoes', title: 'Configurações | Rastro AI', component: page },
        ]),
        { provide: TitleStrategy, useExisting: PageTitleStrategy },
      ],
    });
    const router = TestBed.inject(Router);
    const title = TestBed.inject(Title);
    await router.navigateByUrl('/repositorios/loja-banco/banco');
    expect(title.getTitle()).toBe('Banco de loja-banco | Rastro AI');
    await router.navigateByUrl('/configuracoes');
    expect(title.getTitle()).toBe('Configurações | Rastro AI');
  });

  it('resolveTitle deixa o token quando falta o parâmetro', () => {
    expect(resolveTitle('Banco de :nome', {})).toBe('Banco de :nome');
  });

  // UX-G-L23
  it('SurveyPageTitle cita o ponto de entrada e ignora quando ele ainda não chegou', () => {
    const title = TestBed.inject(Title);
    const service = TestBed.inject(SurveyPageTitle);
    title.setTitle('Fluxo | Rastro AI');
    service.set('Fluxo', undefined);
    expect(title.getTitle()).toBe('Fluxo | Rastro AI');
    service.set('Fluxo', 'PedidosController.Criar');
    expect(title.getTitle()).toBe('Fluxo: PedidosController.Criar | Rastro AI');
    expect(surveyTitle('Visão consolidada', 'A.B')).toBe('Visão consolidada: A.B | Rastro AI');
  });
});
