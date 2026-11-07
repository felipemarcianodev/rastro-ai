import { Injectable, signal } from '@angular/core';

const LAST_REPOSITORY_KEY = 'rastro.lastRepository';

// O último repositório usado (escolhido na tela inicial ou aberto na tela do banco). O "Banco" do
// cabeçalho abre o mapa dele. A escolha fica no navegador (conveniência por pessoa), como a paleta.
@Injectable({ providedIn: 'root' })
export class LastRepositoryService {
  readonly name = signal<string | null>(read());

  remember(name: string): void {
    if (!name || name === this.name()) return;
    this.name.set(name);
    try {
      localStorage.setItem(LAST_REPOSITORY_KEY, name);
    } catch {
      // Armazenamento bloqueado (aba privada): a escolha vale só nesta visita.
    }
  }
}

function read(): string | null {
  try {
    return localStorage.getItem(LAST_REPOSITORY_KEY);
  } catch {
    return null;
  }
}
