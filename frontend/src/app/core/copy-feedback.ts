import { signal } from '@angular/core';

export type CopyStatus = 'idle' | 'copied' | 'failed';

const RESET_MS = 2000;

// Botão "Copiar" de qualquer tela: mostra "Copiado" ou a falha por 2 s e volta ao rótulo.
// Sem clipboard (HTTP fora de localhost, permissão negada) conta como falha, nunca em silêncio.
export class CopyFeedback {
  readonly status = signal<CopyStatus>('idle');
  private timer?: ReturnType<typeof setTimeout>;

  copy(text: string): void {
    if (!navigator.clipboard) {
      this.show('failed');
      return;
    }
    navigator.clipboard.writeText(text).then(
      () => this.show('copied'),
      () => this.show('failed'),
    );
  }

  label(idle: string): string {
    const status = this.status();
    return status === 'copied' ? 'Copiado' : status === 'failed' ? 'Não deu para copiar' : idle;
  }

  reset(): void {
    clearTimeout(this.timer);
    this.status.set('idle');
  }

  private show(status: CopyStatus): void {
    this.status.set(status);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.status.set('idle'), RESET_MS);
  }
}
