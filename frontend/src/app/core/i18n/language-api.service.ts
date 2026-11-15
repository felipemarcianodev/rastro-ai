import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { UiLanguage } from './language';

// Idioma gravado no config.json da máquina (ADR-009): a tela, a API, a CLI e o MCP leem o mesmo.
// `language` é o gravado (nulo = segue o sistema); `effective` é o que vale sem cabeçalho.
export interface LanguageSetting {
  language: UiLanguage | null;
  effective: UiLanguage;
}

@Injectable({ providedIn: 'root' })
export class LanguageApi {
  // Sem HttpClient (função pura em teste), a tela só não grava no servidor.
  private readonly http = inject(HttpClient, { optional: true });

  get available(): boolean {
    return this.http !== null;
  }

  get(): Observable<LanguageSetting> {
    return this.http!.get<LanguageSetting>('/api/settings/language');
  }

  set(language: UiLanguage | null): Observable<LanguageSetting> {
    return this.http!.put<LanguageSetting>('/api/settings/language', { language });
  }
}
