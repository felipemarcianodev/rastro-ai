import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRouteSnapshot, RouterStateSnapshot, TitleStrategy } from '@angular/router';

// UX-FIM-C11: o título da rota pode citar parâmetros com ":nome" (ex.: "Banco de :nome | Rastro AI").
const PARAM_TOKEN = /:([A-Za-z]\w*)/g;

function paramsOf(snapshot: RouterStateSnapshot): Record<string, string> {
  const params: Record<string, string> = {};
  let route: ActivatedRouteSnapshot | null = snapshot.root;
  while (route) {
    Object.assign(params, route.params);
    route = route.firstChild;
  }
  return params;
}

export function resolveTitle(template: string, params: Record<string, string>): string {
  return template.replace(PARAM_TOKEN, (token, name: string) => params[name] ?? token);
}

@Injectable({ providedIn: 'root' })
export class PageTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const template = this.buildTitle(snapshot);
    if (template !== undefined) this.title.setTitle(resolveTitle(template, paramsOf(snapshot)));
  }
}
