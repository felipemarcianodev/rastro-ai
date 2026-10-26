import { Injectable, inject, signal } from '@angular/core';
import { RepositoryItem } from './models';
import { RastroApi } from './rastro-api.service';

// Caminho completo: C:\pasta, C:/pasta ou /pasta.
export function isAbsoluteFolder(folder: string): boolean {
  return /^[A-Za-z]:[\\/]/.test(folder.trim()) || folder.trim().startsWith('/');
}

// "Abrir no editor": a pasta de cada repositório vem da lista cadastrada na API (ADR-003).
@Injectable({ providedIn: 'root' })
export class EditorLinks {
  private readonly api = inject(RastroApi);
  private requested = false;

  readonly repositories = signal<RepositoryItem[]>([]);

  // Carrega a lista uma vez; as Configurações chamam set() depois de adicionar ou remover.
  ensureLoaded(): void {
    if (this.requested) return;
    this.requested = true;
    this.api.repositories().subscribe({
      next: (repos) => this.repositories.set(repos),
      // Sem a lista o botão "Abrir no VS Code" some; a próxima visita tenta de novo.
      error: () => (this.requested = false),
    });
  }

  set(repos: RepositoryItem[]): void {
    this.requested = true;
    this.repositories.set(repos);
  }

  folder(repository: string): string {
    return this.repositories().find((r) => r.name === repository)?.path ?? '';
  }

  // vscode://file/C:/codigo/meu-sistema/backend/Job.cs:42 ; null sem a pasta cadastrada.
  link(repository: string, file: string, line: number): string | null {
    return vscodeLink(this.folder(repository), file, line);
  }
}

export function vscodeLink(root: string, file: string, line: number): string | null {
  if (!isAbsoluteFolder(root) || !file || !Number.isInteger(line) || line < 1) return null;
  const base = root.trim().replace(/\\/g, '/').replace(/\/+$/, '');
  const relative = file.replace(/\\/g, '/').replace(/^\/+/, '');
  // Caminho relativo que sobe de pasta não sai da raiz do repositório.
  if (relative.split('/').includes('..')) return null;
  const path = `${base}/${relative}`.replace(/^\/?/, '/');
  return `vscode://file${encodeURI(path)}:${line}`;
}
