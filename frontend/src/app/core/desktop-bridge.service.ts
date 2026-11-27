import { Injectable, InjectionToken, NgZone, inject } from '@angular/core';

// Ponte com a janela própria do Rastro (ADR-007): o Photino põe window.external.sendMessage e receiveMessage
// na página. No navegador a ponte não existe, e o botão "Procurar…" não aparece.

// Mesmos valores do DesktopMessageType do .NET.
export enum DesktopMessageType {
  PickFolder = 0,
  FolderPicked = 1,
  OpenLink = 2,
  LinkOpened = 3,
}

export interface DesktopExternal {
  sendMessage(message: string): void;
  receiveMessage(callback: (message: string) => void): void;
}

const MAX_PATH_LENGTH = 4096;
const REQUEST_PREFIX = 'pasta-';
const LINK_PREFIX = 'link-';

export function readDesktopExternal(host: unknown): DesktopExternal | null {
  const external = (host as { external?: Partial<DesktopExternal> } | null)?.external;
  return typeof external?.sendMessage === 'function' && typeof external?.receiveMessage === 'function'
    ? (external as DesktopExternal)
    : null;
}

export const DESKTOP_EXTERNAL = new InjectionToken<DesktopExternal | null>('DESKTOP_EXTERNAL', {
  providedIn: 'root',
  factory: () => readDesktopExternal(typeof window === 'undefined' ? null : window),
});

interface FolderPickedMessage {
  type: DesktopMessageType.FolderPicked;
  requestId: string;
  path: string | null;
}

interface LinkOpenedMessage {
  type: DesktopMessageType.LinkOpened;
  requestId: string;
  opened: boolean;
}

function parseJson(message: unknown): unknown {
  if (typeof message !== 'string') return null;
  try {
    return JSON.parse(message);
  } catch {
    // Não é JSON: não é resposta da janela do Rastro.
    return null;
  }
}

// A resposta só vale com o formato combinado; o resto é ignorado.
function parseLinkOpened(data: unknown): LinkOpenedMessage | null {
  const value = data as Partial<LinkOpenedMessage> | null;
  if (!value || value.type !== DesktopMessageType.LinkOpened || typeof value.requestId !== 'string' || typeof value.opened !== 'boolean') return null;
  return { type: DesktopMessageType.LinkOpened, requestId: value.requestId, opened: value.opened };
}

// A resposta só vale com o formato combinado; o resto é ignorado.
function parseFolderPicked(data: unknown): FolderPickedMessage | null {
  const value = data as Partial<FolderPickedMessage> | null;
  if (!value || value.type !== DesktopMessageType.FolderPicked || typeof value.requestId !== 'string') return null;
  const path = value.path;
  if (path !== null && (typeof path !== 'string' || path.length === 0 || path.length > MAX_PATH_LENGTH)) return null;
  return { type: DesktopMessageType.FolderPicked, requestId: value.requestId, path };
}

@Injectable({ providedIn: 'root' })
export class DesktopBridge {
  private readonly external = inject(DESKTOP_EXTERNAL);
  private readonly zone = inject(NgZone);
  private readonly pending = new Map<string, (path: string | null) => void>();
  private readonly pendingLinks = new Map<string, (opened: boolean) => void>();
  private sequence = 0;

  // Dentro da janela própria: o botão "Procurar…" aparece.
  readonly available = this.external !== null;

  constructor() {
    this.external?.receiveMessage((message) => this.receive(message));
  }

  // Abre a caixa "Escolher pasta" do sistema. Devolve o caminho completo, ou null se o usuário cancelou.
  pickFolder(): Promise<string | null> {
    if (!this.external) return Promise.resolve(null);
    const requestId = `${REQUEST_PREFIX}${++this.sequence}`;
    return new Promise((resolve) => {
      this.pending.set(requestId, resolve);
      this.external!.sendMessage(JSON.stringify({ type: DesktopMessageType.PickFolder, requestId }));
    });
  }

  // Abre um link fora do Rastro e diz se abriu. Na janela própria, quem decide é o .NET (só host reconhecido e
  // editor configurado) e responde se abriu; no navegador, abre numa aba nova sem dar à página aberta acesso a esta
  // (com "noopener" o navegador não devolve a aba, então só a exceção de bloqueio conta como falha).
  openExternal(url: string): Promise<boolean> {
    if (!this.external) {
      try {
        window.open(url, '_blank', 'noopener');
        return Promise.resolve(true);
      } catch {
        // Bloqueado pelo navegador: a tela avisa que não abriu.
        return Promise.resolve(false);
      }
    }
    const requestId = `${LINK_PREFIX}${++this.sequence}`;
    return new Promise((resolve) => {
      this.pendingLinks.set(requestId, resolve);
      this.external!.sendMessage(JSON.stringify({ type: DesktopMessageType.OpenLink, requestId, url }));
    });
  }

  private receive(message: unknown): void {
    const data = parseJson(message);
    const link = parseLinkOpened(data);
    if (link) {
      const resolveLink = this.pendingLinks.get(link.requestId);
      if (!resolveLink) return;
      this.pendingLinks.delete(link.requestId);
      this.zone.run(() => resolveLink(link.opened));
      return;
    }
    const answer = parseFolderPicked(data);
    const resolve = answer ? this.pending.get(answer.requestId) : undefined;
    if (!answer || !resolve) return;
    this.pending.delete(answer.requestId);
    // O evento da janela chega fora do Angular: o campo só se atualiza dentro da zona.
    this.zone.run(() => resolve(answer.path));
  }
}
