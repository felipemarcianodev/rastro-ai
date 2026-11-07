import { TestBed } from '@angular/core/testing';
import { DESKTOP_EXTERNAL, DesktopBridge, DesktopExternal, DesktopMessageType, readDesktopExternal } from './desktop-bridge.service';

// Ponte falsa da janela própria: guarda o que a tela mandou e responde quando o teste pede.
export class FakeDesktopExternal implements DesktopExternal {
  readonly sent: string[] = [];
  private callback?: (message: string) => void;

  sendMessage(message: string): void {
    this.sent.push(message);
  }

  receiveMessage(callback: (message: string) => void): void {
    this.callback = callback;
  }

  answer(message: string): void {
    this.callback?.(message);
  }

  // Responde ao último pedido com a pasta escolhida.
  pick(path: string | null): void {
    const request = JSON.parse(this.sent[this.sent.length - 1]) as { requestId: string };
    this.answer(JSON.stringify({ type: DesktopMessageType.FolderPicked, requestId: request.requestId, path }));
  }
}

function bridgeWith(external: DesktopExternal | null): DesktopBridge {
  TestBed.configureTestingModule({ providers: [{ provide: DESKTOP_EXTERNAL, useValue: external }] });
  return TestBed.inject(DesktopBridge);
}

describe('DesktopBridge', () => {
  // UI-JAN-001
  it('semAPonte_naoFicaDisponivel_eDevolveNull', async () => {
    const bridge = bridgeWith(null);

    expect(bridge.available).toBeFalse();
    expect(await bridge.pickFolder()).toBeNull();
  });

  // UI-JAN-002
  it('navegadorComum_naoTemAPonte', () => {
    expect(readDesktopExternal({ external: {} })).toBeNull();
    expect(readDesktopExternal(null)).toBeNull();
    expect(readDesktopExternal({ external: { sendMessage: () => undefined, receiveMessage: () => undefined } })).not.toBeNull();
  });

  // UI-JAN-003
  it('pickFolder_mandaMensagemTipada_eDevolveOCaminho', async () => {
    const external = new FakeDesktopExternal();
    const bridge = bridgeWith(external);

    const picked = bridge.pickFolder();
    expect(JSON.parse(external.sent[0])).toEqual({ type: DesktopMessageType.PickFolder, requestId: 'pasta-1' });
    external.pick('C:\\codigo\\loja');

    expect(await picked).toBe('C:\\codigo\\loja');
  });

  // UI-JAN-004
  it('respostaForaDoFormato_eIgnorada', async () => {
    const external = new FakeDesktopExternal();
    const bridge = bridgeWith(external);

    const picked = bridge.pickFolder();
    external.answer('não é json');
    external.answer(JSON.stringify({ type: DesktopMessageType.FolderPicked, requestId: 'outro', path: 'C:\\x' }));
    external.answer(JSON.stringify({ type: DesktopMessageType.PickFolder, requestId: 'pasta-1', path: 'C:\\x' }));
    external.answer(JSON.stringify({ type: DesktopMessageType.FolderPicked, requestId: 'pasta-1', path: 42 }));
    external.pick(null);

    expect(await picked).toBeNull();
  });
});
