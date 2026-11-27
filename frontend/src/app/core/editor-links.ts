import { Injectable, inject, signal } from '@angular/core';
import { DesktopBridge } from './desktop-bridge.service';
import { fillLinkTemplate } from './graph/link-template';
import { RepositoryItem, RepositoryLinks } from './models';
import { RastroApi } from './rastro-api.service';

// Esquema do link do VS Code: o rótulo diz "VS Code" só quando o link é dele; com outro editor configurado,
// "editor".
export const VSCODE_SCHEME = 'vscode:';

// Caminho completo: C:\pasta, C:/pasta ou /pasta.
export function isAbsoluteFolder(folder: string): boolean {
  return /^[A-Za-z]:[\\/]/.test(folder.trim()) || folder.trim().startsWith('/');
}

// "Abrir no editor": a pasta de cada repositório vem da lista cadastrada na API (ADR-003).
@Injectable({ providedIn: 'root' })
export class EditorLinks {
  private readonly api = inject(RastroApi);
  private readonly bridge = inject(DesktopBridge);
  private requested = false;

  readonly repositories = signal<RepositoryItem[]>([]);
  // Modelos do editor do levantamento aberto (rota de links, Fase 56): o configurado em Rastro:Editor:UrlTemplate ou,
  // sem configuração, o do VS Code. Vazio fora de um levantamento: vale o link do VS Code pela pasta cadastrada.
  private readonly templates = signal<ReadonlyMap<string, string>>(new Map());

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

  // Modelos do levantamento aberto; a página do levantamento chama ao abrir e limpa ao sair.
  useSurveyLinks(links: readonly RepositoryLinks[]): void {
    this.templates.set(new Map(links.filter((l) => l.editorTemplate).map((l) => [l.repository.toLowerCase(), l.editorTemplate!])));
  }

  clearSurveyLinks(): void {
    this.templates.set(new Map());
  }

  // Pelo modelo do levantamento quando há; senão vscode://file/C:/codigo/meu-sistema/backend/Job.cs:42 pela pasta
  // cadastrada; null sem nenhum dos dois.
  link(repository: string, file: string, line: number): string | null {
    const template = this.templates().get(repository.toLowerCase());
    if (template && file && Number.isInteger(line) && line >= 1) return fillLinkTemplate(template, file, line);
    return vscodeLink(this.folder(repository), file, line);
  }

  // Na janela própria, quem abre é o .NET (só o esquema do editor configurado e o do VS Code); no navegador, a
  // própria página segue o link (o sistema chama o editor).
  open(url: string): void {
    if (this.bridge.available) {
      void this.bridge.openExternal(url);
      return;
    }
    window.location.assign(url);
  }
}

export function isVsCodeLink(link: string | null): boolean {
  return link?.startsWith(VSCODE_SCHEME) ?? false;
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
