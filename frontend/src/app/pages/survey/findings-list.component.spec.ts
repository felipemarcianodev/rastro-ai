import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { FindingKind, FindingSeverity, FlowGraph, NodeKind } from '../../core/models';
import { FindingsListComponent } from './findings-list.component';

const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'App.Run',
  entryNodeId: 'a',
  nodes: [
    { id: 'a', kind: NodeKind.Entry, label: 'App.Run' },
    { id: 'b', kind: NodeKind.Method, label: 'Repo.Save' },
  ],
  edges: [],
  findings: [
    { kind: FindingKind.LongMethod, severity: FindingSeverity.Warning, nodeId: 'a', message: 'App.Run tem 90 linhas' },
    { kind: FindingKind.EntityChangedOutsideDomain, severity: FindingSeverity.Warning, nodeId: 'a', message: 'altera Pedido.Status direto' },
    { kind: FindingKind.SilentCatch, severity: FindingSeverity.Critical, nodeId: 'b', message: 'catch vazio' },
  ],
};

function render(nodeId: string | null, source: FlowGraph = graph): { el: HTMLElement; prompts: number[] } {
  TestBed.configureTestingModule({ imports: [FindingsListComponent], providers: [provideRouter([])] });
  const fixture = TestBed.createComponent(FindingsListComponent);
  fixture.componentRef.setInput('graph', source);
  fixture.componentRef.setInput('repository', 'LojaBanco');
  fixture.componentRef.setInput('nodeId', nodeId);
  const prompts: number[] = [];
  fixture.componentInstance.prompt.subscribe((index) => prompts.push(index));
  fixture.detectChanges();
  return { el: fixture.nativeElement as HTMLElement, prompts };
}

describe('FindingsListComponent', () => {
  it('agrupa com DDD primeiro e manda o índice do grafo no prompt', () => {
    const { el, prompts } = render(null);

    const titles = [...el.querySelectorAll('.group-title')].map((h) => h.textContent?.trim());
    expect(titles).toEqual(['Regras de DDD (1)', 'Código limpo (1)', 'Métricas (1)']);
    (el.querySelector('.finding .finding-actions button:last-child') as HTMLButtonElement).click();
    expect(prompts).toEqual([1]);
  });

  it('com o passo, mostra só os achados dele', () => {
    const { el } = render('b');

    expect(el.querySelectorAll('.finding').length).toBe(1);
    expect(el.textContent).toContain('catch vazio');
    expect(el.textContent).not.toContain('Ver no fluxo');
  });

  it('levantamento antigo, sem inspeção, pede para levantar de novo', () => {
    const { el } = render(null, { ...graph, findings: undefined });

    expect(el.textContent).toContain('Levante o fluxo de novo');
  });

  it('achado de banco leva ao mapa do banco com a tabela, e sem alvo vai sem tabela', () => {
    // UX-F05
    const { el } = render(null, {
      ...graph,
      findings: [
        { kind: FindingKind.QueryInLoop, severity: FindingSeverity.Warning, nodeId: 'a', message: 'consulta em laço', target: 'Pedidos' },
        { kind: FindingKind.ReadWithoutNoTracking, severity: FindingSeverity.Info, nodeId: 'b', message: 'leitura' },
        { kind: FindingKind.SilentCatch, severity: FindingSeverity.Critical, nodeId: 'b', message: 'catch vazio' },
      ],
    });

    const links = [...el.querySelectorAll<HTMLAnchorElement>('.finding-actions a')];
    expect(links.length).toBe(2);
    expect(links.every((a) => a.textContent?.trim() === 'Ver no mapa do banco')).toBeTrue();
    const hrefs = links.map((a) => a.getAttribute('href'));
    expect(hrefs).toContain('/repositorios/LojaBanco/banco?tabela=Pedidos');
    expect(hrefs).toContain('/repositorios/LojaBanco/banco');
  });

  it('cada botão de prompt tem nome distinto e o local mostra o arquivo de caminho Windows', () => {
    const { el } = render(null, {
      ...graph,
      findings: [
        { kind: FindingKind.SilentCatch, severity: FindingSeverity.Critical, nodeId: 'a', message: 'x', location: { file: String.raw`src\App\Run.cs`, line: 7 } },
        { kind: FindingKind.SilentCatch, severity: FindingSeverity.Critical, nodeId: 'b', message: 'y' },
      ],
    });

    const labels = [...el.querySelectorAll('.finding-actions button:last-child')].map((b) => b.getAttribute('aria-label'));
    expect(new Set(labels).size).toBe(2);
    expect(labels[0]).toContain('App.Run');
    const where = el.querySelector('.finding-where') as HTMLElement;
    expect(where.textContent).toBe('Run.cs:7');
    expect(where.title).toBe(String.raw`src\App\Run.cs:7`);
  });
});
