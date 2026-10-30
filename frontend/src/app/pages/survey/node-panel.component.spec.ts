import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { FlowGraph, NodeKind } from '../../core/models';
import { NodePanelComponent } from './node-panel.component';

const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'Job.Run',
  entryNodeId: 'a',
  nodes: [
    { id: 'a', kind: NodeKind.Entry, label: 'Job.Run' },
    { id: 'b', kind: NodeKind.Method, label: 'Sender.Send' },
  ],
  edges: [],
};

function render(notes: Map<string, string>) {
  TestBed.configureTestingModule({ imports: [NodePanelComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
  const fixture = TestBed.createComponent(NodePanelComponent);
  fixture.componentRef.setInput('surveyId', 's1');
  fixture.componentRef.setInput('graph', graph);
  fixture.componentRef.setInput('selectedId', 'b');
  fixture.componentRef.setInput('notes', notes);
  const saved: { nodeId: string; text: string }[] = [];
  fixture.componentInstance.saveNote.subscribe((note) => saved.push(note));
  fixture.detectChanges();
  return { fixture, saved, el: fixture.nativeElement as HTMLElement };
}

const button = (el: HTMLElement, text: string) => Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes(text)) as HTMLButtonElement;

describe('NodePanelComponent: anotação do passo', () => {
  it('sem anotação oferece anotar e salva o texto sem espaços nas pontas', async () => {
    const { fixture, saved, el } = render(new Map());

    button(el, 'Anotar este passo').click();
    fixture.detectChanges();
    await fixture.whenStable();
    const textarea = el.querySelector('#note-text') as HTMLTextAreaElement;
    textarea.value = '  chamado pelo webhook  ';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    button(el, 'Salvar anotação').click();

    expect(saved).toEqual([{ nodeId: 'b', text: 'chamado pelo webhook' }]);
  });

  it('com anotação mostra o texto e apagar manda texto vazio', () => {
    const { fixture, saved, el } = render(new Map([['b', 'cuidado com o webhook']]));

    expect(el.querySelector('.note-text')?.textContent).toContain('cuidado com o webhook');
    button(el, 'Editar anotação').click();
    fixture.detectChanges();
    button(el, 'Excluir anotação').click();

    expect(saved).toEqual([{ nodeId: 'b', text: '' }]);
  });

  it('salvar fica desabilitado com o campo vazio', async () => {
    const { fixture, el } = render(new Map());

    button(el, 'Anotar este passo').click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(button(el, 'Salvar anotação').disabled).toBeTrue();
  });

  it('erro ao salvar mantém o editor aberto; sucesso fecha', async () => {
    const { fixture, el } = render(new Map());
    button(el, 'Anotar este passo').click();
    fixture.detectChanges();
    await fixture.whenStable();
    const textarea = el.querySelector('#note-text') as HTMLTextAreaElement;
    textarea.value = 'nota';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    button(el, 'Salvar anotação').click();

    fixture.componentRef.setInput('noteMessage', { ok: false, text: 'Falhou' });
    fixture.detectChanges();
    expect(el.querySelector('#note-text')).not.toBeNull();
    expect(el.textContent).toContain('Falhou');

    button(el, 'Salvar anotação').click();
    fixture.componentRef.setInput('noteMessage', { ok: true, text: 'Anotação salva.' });
    fixture.detectChanges();
    expect(el.querySelector('#note-text')).toBeNull();
  });
});

describe('NodePanelComponent: passo de outro sistema (Fase 25)', () => {
  const cross: FlowGraph = {
    schemaVersion: 2,
    entryPoint: 'Job.Run',
    entryNodeId: 'a',
    nodes: [
      { id: 'a', kind: NodeKind.Entry, label: 'Job.Run' },
      { id: 'p::b', kind: NodeKind.Method, label: 'EstornosController.Criar', system: 'pagamentos' },
    ],
    edges: [{ from: 'a', to: 'p::b', order: 0, confidence: 0, http: { method: 'POST', route: 'api/estornos' } }],
  };

  function open(id: string) {
    TestBed.configureTestingModule({ imports: [NodePanelComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(NodePanelComponent);
    fixture.componentRef.setInput('surveyId', 's1');
    fixture.componentRef.setInput('repository', 'pedidos');
    fixture.componentRef.setInput('commitHash', 'aaaaaaaaaaaa');
    fixture.componentRef.setInput('segments', [{ order: 1, repository: 'pagamentos', solutionPath: 'P.slnx', entryPoint: 'x', commitHash: 'bbbbbbbbbbbb' }]);
    fixture.componentRef.setInput('graph', cross);
    fixture.componentRef.setInput('selectedId', id);
    fixture.componentRef.setInput('editing', true);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  // UI-SYS-011
  it('painel_passoDeOutroSistema_mostraSistemaECommitEBloqueiaEdicao', () => {
    const el = open('p::b');

    expect(el.querySelector('.origin')?.textContent).toContain('Sistema pagamentos');
    expect(el.querySelector('.origin')?.textContent).toContain('bbbbbbb');
    expect(el.querySelector('.foreign')?.textContent).toContain('Edição só no primeiro sistema do fluxo');
    expect(el.querySelector('.remove-step')).toBeNull();
    expect(el.querySelector('#connect')).toBeNull();
    expect(Array.from(el.querySelectorAll('button')).some((b) => b.textContent?.includes('Remover ligação'))).toBeFalse();
  });

  // UI-SYS-012
  it('painel_passoDoPrimeiroSistema_naoEditaLigacaoHttp', () => {
    const el = open('a');

    expect(el.querySelector('.origin')?.textContent).toContain('Sistema pedidos');
    expect(el.querySelector('#intent')).not.toBeNull();
    expect(Array.from(el.querySelectorAll('button')).some((b) => b.textContent?.includes('Remover ligação'))).toBeFalse();
    expect(el.textContent).toContain('Edição só no primeiro sistema do fluxo');
  });
});
