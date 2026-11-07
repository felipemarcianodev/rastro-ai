import { Injectable, computed, inject, signal } from '@angular/core';
import { AI_PROVIDER_LABEL, AiStatus } from './models';
import { RastroApi } from './rastro-api.service';

// Estado da IA para a barra superior e para mostrar (ou não) os botões de IA. Sem IA ligada e
// configurada, nenhum botão de IA aparece (sem botão morto).
@Injectable({ providedIn: 'root' })
export class AiStatusService {
  private readonly api = inject(RastroApi);

  readonly status = signal<AiStatus | null>(null);

  readonly label = computed(() => {
    const status = this.status();
    if (!status || !status.enabled) return 'IA desligada';
    if (!status.configured) return 'IA sem chave';
    return `IA: ${AI_PROVIDER_LABEL[status.provider]}`;
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
