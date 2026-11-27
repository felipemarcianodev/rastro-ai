import { Injectable, Injector, computed, inject, signal } from '@angular/core';
import { DrawioStatus } from './models';
import { RastroApi } from './rastro-api.service';

// O draw.io dentro do Rastro está nesta instalação? (ADR-011) Lê-se uma vez; sem ele, o botão do desenho
// draw.io não aparece. Falha da API vale "sem draw.io".
@Injectable({ providedIn: 'root' })
export class DrawioStatusService {
  // A API só é pedida em load(): quem só lê o status (a barra de desenhos) não precisa do HttpClient.
  private readonly injector = inject(Injector);
  private requested = false;

  readonly status = signal<DrawioStatus | null>(null);
  // A resposta já chegou (com ou sem o draw.io).
  readonly loaded = signal(false);
  readonly available = computed(() => !!this.status()?.available);

  load(): void {
    if (this.requested) return;
    this.requested = true;
    this.injector.get(RastroApi).drawio().subscribe({
      next: (status) => {
        this.status.set(status);
        this.loaded.set(true);
      },
      // API fora do ar ou sem a rota: a tela segue sem o desenho draw.io.
      error: () => this.loaded.set(true),
    });
  }
}
