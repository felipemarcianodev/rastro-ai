import { Injectable, effect, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRouteSnapshot, RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { LanguageService } from './i18n/i18n';

// UX-G-L23: o sufixo do título da aba é o nome do produto e não se traduz.
export const APP_TITLE_SUFFIX = 'Rastro AI';

function paramsOf(snapshot: RouterStateSnapshot): Record<string, string> {
  const params: Record<string, string> = {};
  let route: ActivatedRouteSnapshot | null = snapshot.root;
  while (route) {
    Object.assign(params, route.params);
    route = route.firstChild;
  }
  return params;
}

// O título da rota é uma chave de tradução (route.*); os parâmetros da rota entram como marcadores
// (ex.: "Banco de {nome}", UX-FIM-C11). Troca de idioma refaz o título da aba.
@Injectable({ providedIn: 'root' })
export class PageTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly i18n = inject(LanguageService);
  private current: { key: string; params: Record<string, string> } | null = null;

  constructor() {
    super();
    effect(() => {
      this.i18n.language();
      if (this.current) this.apply(this.current);
    });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const key = this.buildTitle(snapshot);
    if (key === undefined) return;
    this.current = { key, params: paramsOf(snapshot) };
    this.apply(this.current);
  }

  // A página que cita o ponto de entrada no título assume a aba: a troca de idioma deixa de refazer o título da rota.
  release(): void {
    this.current = null;
  }

  private apply(current: { key: string; params: Record<string, string> }): void {
    this.title.setTitle(`${this.i18n.t(current.key, current.params)} | ${APP_TITLE_SUFFIX}`);
  }
}

export function surveyTitle(prefix: string, entryPoint: string): string {
  return `${prefix}: ${entryPoint} | ${APP_TITLE_SUFFIX}`;
}

// UX-G-L23: o levantamento chega depois da rota; a página cita o ponto de entrada no título da aba.
// O prefixo é uma chave de tradução (route.*). Chamado dentro de um effect, o título se refaz na troca de idioma.
@Injectable({ providedIn: 'root' })
export class SurveyPageTitle {
  private readonly title = inject(Title);
  private readonly i18n = inject(LanguageService);
  private readonly routeTitle = inject(PageTitleStrategy);

  set(prefixKey: string, entryPoint: string | undefined): void {
    if (!entryPoint) return;
    this.routeTitle.release();
    this.title.setTitle(surveyTitle(this.i18n.t(prefixKey), entryPoint));
  }
}
