import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { DbOperation, FindingKind, FindingSeverity, FlowGraph, NodeKind, UnresolvedReason } from '../../core/models';
import { NodePanelComponent } from './node-panel.component';

// Visão geral do painel na Fase B da repaginação: banco do fluxo, atalho das Perguntas e faixa de contadores.
const base: FlowGraph = {
  schemaVersion: 3,
  entryPoint: 'A',
  entryNodeId: 'a',
  nodes: [
    { id: 'a', kind: NodeKind.Entry, label: 'A' },
    { id: 'b', kind: NodeKind.Method, label: 'B', db: [{ target: 'pedidos', operation: DbOperation.Read, line: 1 }, { target: 'pedidos', operation: DbOperation.Insert, line: 2 }] },
  ],
  edges: [],
};

function render(graph: FlowGraph, repository = 'loja', crossFlow: { nodeId: string; label: string; entity: string; guard: string; flows: string[] }[] = []) {
  TestBed.configureTestingModule({ imports: [NodePanelComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
  const fixture = TestBed.createComponent(NodePanelComponent);
  fixture.componentRef.setInput('surveyId', 's1');
  fixture.componentRef.setInput('repository', repository);
  fixture.componentRef.setInput('graph', graph);
  fixture.componentRef.setInput('crossFlow', crossFlow);
  fixture.detectChanges();
  return fixture;
}

describe('NodePanelComponent: Visão geral (Fase B)', () => {
  // WEB-UXB-010
  it('mostra "Banco neste fluxo" com a tabela uma vez e o que ela sofre, com a rota sem ?tabela quando há várias', () => {
    const graph: FlowGraph = { ...base, nodes: [...base.nodes, { id: 'c', kind: NodeKind.Method, label: 'C', db: [{ target: 'estoque', operation: DbOperation.Execute, line: 3 }] }] };
    const el = render(graph).nativeElement as HTMLElement;

    expect(el.querySelector('#section-db summary')!.textContent).toContain('2 tabelas');
    const rows = [...el.querySelectorAll('#section-db li')].map((li) => li.textContent!.replace(/\s+/g, ' ').trim());
    expect(rows).toEqual(['estoque executa', 'pedidos lê grava']);
    expect(el.querySelector<HTMLAnchorElement>('#section-db a')!.getAttribute('href')).toBe('/repositorios/loja/banco?levantamento=s1');
  });

  // WEB-UXB-011
  it('com uma tabela só o botão leva ?tabela=', () => {
    const el = render(base).nativeElement as HTMLElement;

    expect(el.querySelector('#section-db summary')!.textContent).toContain('1 tabela)');
    expect(el.querySelector<HTMLAnchorElement>('#section-db a')!.getAttribute('href')).toBe('/repositorios/loja/banco?tabela=pedidos&levantamento=s1');
  });

  // WEB-UXB-012
  it('sem acesso a banco a seção não aparece', () => {
    const el = render({ ...base, nodes: [base.nodes[0]] }).nativeElement as HTMLElement;

    expect(el.querySelector('#section-db')).toBeNull();
  });

  // WEB-UXB-013
  it('o botão "Perguntas sobre o fluxo" pede a aba Perguntas', () => {
    const fixture = render(base);
    let asked = 0;
    fixture.componentInstance.openQuestions.subscribe(() => asked++);

    const button = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((b) => b.textContent!.trim() === 'Perguntas sobre o fluxo')!;
    button.click();

    expect(asked).toBe(1);
  });

  // WEB-UXB-014
  it('a faixa de contadores só traz o que existe e cada botão abre e rola até a seção', () => {
    const graph: FlowGraph = {
      ...base,
      nodes: [...base.nodes, { id: 'h', kind: NodeKind.Unresolved, label: 'Faltou', reason: UnresolvedReason.NoImplementation }],
      findings: [{ kind: FindingKind.HighComplexity, severity: FindingSeverity.Warning, nodeId: 'b', message: 'alta' }],
    };
    const fixture = render(graph);
    const el = fixture.nativeElement as HTMLElement;

    const labels = [...el.querySelectorAll('.counters button')].map((b) => b.textContent!.trim());
    expect(labels).toEqual(['1 buraco', '1 melhoria']);

    const section = el.querySelector<HTMLDetailsElement>('#section-findings')!;
    section.open = false;
    const scroll = spyOn(section, 'scrollIntoView');
    el.querySelectorAll<HTMLButtonElement>('.counters button')[1].click();
    expect(section.open).toBeTrue();
    expect(scroll).toHaveBeenCalled();
  });

  // WEB-UXB-015
  it('sem buracos, entre fluxos nem melhorias a faixa não aparece', () => {
    const el = render({ ...base, nodes: [base.nodes[0]] }).nativeElement as HTMLElement;

    expect(el.querySelector('.counters')).toBeNull();
  });

  // WEB-UXB-016
  it('conta "entre fluxos" quando há regra cruzada', () => {
    const el = render(base, 'loja', [{ nodeId: 'b', label: 'B', entity: 'Pedido', guard: 'Valida', flows: ['X'] }]).nativeElement as HTMLElement;

    expect([...el.querySelectorAll('.counters button')].map((b) => b.textContent!.trim())).toEqual(['1 entre fluxos']);
  });

  // WEB-UXC-012 (F13)
  it('traz "O que dá para fazer aqui" com os atalhos, que pedem exportar e editar', () => {
    const fixture = render(base);
    const el = fixture.nativeElement as HTMLElement;
    let exported = 0;
    let edited = 0;
    fixture.componentInstance.exportFlow.subscribe(() => exported++);
    fixture.componentInstance.editFlow.subscribe(() => edited++);

    const block = el.querySelector('.shortcuts')!;
    expect(block.querySelector('h3')!.textContent).toBe('O que dá para fazer aqui');
    const items = [...block.querySelectorAll<HTMLElement>('.btn')];
    expect(items.map((b) => b.textContent!.trim())).toEqual(['Mapa do banco', 'Exportar o fluxo', 'Editar fluxo', 'Perguntas sobre o fluxo']);
    expect(items[0].getAttribute('href')).toBe('/repositorios/loja/banco?levantamento=s1');
    items[1].click();
    items[2].click();
    expect([exported, edited]).toEqual([1, 1]);
  });
});
