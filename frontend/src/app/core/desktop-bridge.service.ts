import { Injectable, InjectionToken, NgZone, inject } from '@angular/core';

// Ponte com a janela própria do Rastro (ADR-007): o Photino põe window.external.sendMessage e receiveMessage
// na página. No navegador a ponte não existe, e o botão "Procurar…" não aparece.

// Mesmos valores do DesktopMessageType do .NET.
export enum DesktopMessageType {
  PickFolder = 0,
  FolderPicked = 1,
}

export interface DesktopExternal {
  sendMessage(message: string): void;
  receiveMessage(callback: (message: string) => void): void;
}

const MAX_PATH_LENGTH = 4096;
const REQUEST_PREFIX = 'pasta-';

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

// A resposta só vale com o formato combinado; o resto é ignorado.
function parseFolderPicked(message: unknown): FolderPickedMessage | null {
  if (typeof message !== 'string') return null;
  let data: unknown;
  try {
    data = JSON.parse(message);
  } catch {
    // Não é JSON: não é resposta da janela do Rastro.
    return null;
  }
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

  private receive(message: unknown): void {
    const answer = parseFolderPicked(message);
    const resolve = answer ? this.pending.get(answer.requestId) : undefined;
    if (!answer || !resolve) return;
    this.pending.delete(answer.requestId);
    // O evento da janela chega fora do Angular: o campo só se atualiza dentro da zona.
    this.zone.run(() => resolve(answer.path));
  }
}
