import { Injectable, computed, inject, signal } from '@angular/core';
import { LanguageService } from './i18n/i18n';
import { AI_PROVIDER_LABEL, AiStatus } from './models';
import { RastroApi } from './rastro-api.service';

// Estado da IA para a barra superior e para mostrar (ou não) os botões de IA. Sem IA ligada e
// configurada, nenhum botão de IA aparece (sem botão morto).
@Injectable({ providedIn: 'root' })
export class AiStatusService {
  private readonly api = inject(RastroApi);
  private readonly i18n = inject(LanguageService);

  readonly status = signal<AiStatus | null>(null);

  readonly label = computed(() => {
    const status = this.status();
    if (!status || !status.enabled) return this.i18n.t('ai.status.off');
    if (!status.configured) return this.i18n.t('ai.status.noKey');
    return this.i18n.t('ai.status.on', { provider: AI_PROVIDER_LABEL[status.provider] });
  });

  readonly active = computed(() => !!this.status()?.enabled && !!this.status()?.configured);

  refresh(): void {
    this.api.aiStatus().subscribe({
      next: (status) => this.status.set(status),
      // API fora do ar: a tela segue sem os botões de IA.
      error: () => this.status.set(null),
    });
  }
}
