import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter } from '@angular/router';
import { LanguageService } from './i18n/i18n';
import { UiLanguage } from './i18n/language';
import { PageTitleStrategy, SurveyPageTitle, surveyTitle } from './page-title.strategy';

describe('PageTitleStrategy', () => {
  // UX-FIM-C11
  it('troca :param pelo valor da rota e mantém título fixo sem parâmetro', async () => {
    const page = class {};
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'repositorios/:nome/banco', title: 'route.database', component: page },
          { path: 'configuracoes', title: 'route.settings', component: page },
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

  // WEB-I18N-020
  it('refaz o título da aba na troca de idioma, com o nome do produto no fim', async () => {
    const page = class {};
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'configuracoes', title: 'route.settings', component: page }]), { provide: TitleStrategy, useExisting: PageTitleStrategy }],
    });
    await TestBed.inject(Router).navigateByUrl('/configuracoes');
    const language = TestBed.inject(LanguageService);
    await language.set(UiLanguage.English);
    TestBed.tick();
    expect(TestBed.inject(Title).getTitle()).toBe('Settings | Rastro AI');
    await language.set(UiLanguage.Portuguese);
    localStorage.removeItem('rastro.language');
  });

  // UX-G-L23
  it('SurveyPageTitle cita o ponto de entrada e ignora quando ele ainda não chegou', () => {
    const title = TestBed.inject(Title);
    const service = TestBed.inject(SurveyPageTitle);
    title.setTitle('Fluxo | Rastro AI');
    service.set('route.survey', undefined);
    expect(title.getTitle()).toBe('Fluxo | Rastro AI');
    service.set('route.survey', 'PedidosController.Criar');
    expect(title.getTitle()).toBe('Fluxo: PedidosController.Criar | Rastro AI');
    expect(surveyTitle('Visão consolidada', 'A.B')).toBe('Visão consolidada: A.B | Rastro AI');
  });
});
