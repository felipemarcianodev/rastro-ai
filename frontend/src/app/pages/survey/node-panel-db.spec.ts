import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NODE_EFFECT_HINT, NodeEffect, effectOf, effectText } from '../../core/graph/reading';
import { DbOperation, FlowGraph, NodeKind } from '../../core/models';
import { NodePanelComponent } from './node-panel.component';

// Banco nos fluxos (Fase 39): o passo diz qual tabela lê ou grava e leva ao mapa do banco.
const graph: FlowGraph = {
  schemaVersion: 3,
  entryPoint: 'PedidosController.Fechar',
  entryNodeId: 'a',
  nodes: [
    { id: 'a', kind: NodeKind.Entry, label: 'PedidosController.Fechar' },
    {
      id: 'b',
      kind: NodeKind.Method,
      label: 'Loja.Gravar',
      db: [
        { target: 'pedidos', operation: DbOperation.Insert, line: 12, entity: 'Pedido' },
        { target: 'sp_baixar_estoque', operation: DbOperation.Execute, line: 14 },
      ],
    },
  ],
  edges: [],
};

function render(repository: string) {
  TestBed.configureTestingModule({ imports: [NodePanelComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
  const fixture = TestBed.createComponent(NodePanelComponent);
  fixture.componentRef.setInput('surveyId', 's1');
  fixture.componentRef.setInput('repository', repository);
  fixture.componentRef.setInput('graph', graph);
  fixture.componentRef.setInput('selectedId', 'b');
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('NodePanelComponent: o passo no banco (Fase 39)', () => {
  // WEB-DBF-001
  it('lista a operação, a tabela com link para o mapa do banco e a linha', () => {
    const el = render('loja');

    const items = [...el.querySelectorAll('.db-access li')].map((li) => [...li.children].map((c) => c.textContent!.trim()).join(' '));
    expect(items).toEqual(['inclui pedidos linha 12', 'executa sp_baixar_estoque linha 14']);
    expect(el.querySelector<HTMLAnchorElement>('.db-access a')!.getAttribute('href')).toBe('/repositorios/loja/banco?tabela=pedidos&levantamento=s1');
  });

  // WEB-DBQ-030
  it('o passo com SQL cru leva a consulta e a origem para a análise; sem SQL, não há link', () => {
    const withSql: FlowGraph = {
      ...graph,
      nodes: [graph.nodes[0], { ...graph.nodes[1], db: [{ target: 'pedidos', operation: DbOperation.Read, line: 9, sql: 'SELECT * FROM pedidos WHERE ano = @p0' }, graph.nodes[1].db![1]] }],
    };
    TestBed.configureTestingModule({ imports: [NodePanelComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(NodePanelComponent);
    fixture.componentRef.setInput('surveyId', 's1');
    fixture.componentRef.setInput('repository', 'loja');
    fixture.componentRef.setInput('graph', withSql);
    fixture.componentRef.setInput('selectedId', 'b');
    fixture.detectChanges();

    const links = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('.db-sql-link')];
    expect(links.length).toBe(1);
    expect(links[0].textContent!.trim()).toBe('Analisar a consulta');
    const url = new URL(links[0].href);
    expect(url.pathname).toBe('/repositorios/loja/banco');
    expect(url.searchParams.get('aba')).toBe('consulta');
    expect(url.searchParams.get('consulta')).toBe('SELECT * FROM pedidos WHERE ano = @p0');
    expect(url.searchParams.get('origem')).toBe('Loja.Gravar');
  });

  // WEB-DBF-002
  it('o nome do método não diz banco, mas o EF no corpo diz: a etiqueta é "banco"', () => {
    expect(effectOf(graph.nodes[1])).toBe(NodeEffect.Database);
    expect(effectOf({ id: 'c', kind: NodeKind.Method, label: 'Loja.Calcular' })).toBeUndefined();
    expect(effectOf({ id: 'd', kind: NodeKind.Decision, label: 'if (x)', db: [] })).toBeUndefined();
    // A dica da etiqueta diz a tabela e a operação.
    expect(effectText(graph.nodes[1], NodeEffect.Database)).toBe('Inclui em pedidos; executa sp_baixar_estoque');
    expect(effectText({ id: 'e', kind: NodeKind.Method, label: 'PedidoRepository.Salvar' }, NodeEffect.Database)).toBe(NODE_EFFECT_HINT[NodeEffect.Database]);
  });
});
