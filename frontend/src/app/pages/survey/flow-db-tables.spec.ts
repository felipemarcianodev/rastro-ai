import { DbOperation, FlowGraph, NodeKind } from '../../core/models';
import { DbEffect, dbMapQuery, flowDbTables } from './flow-db-tables';

function graphWith(...nodes: FlowGraph['nodes']): FlowGraph {
  return { schemaVersion: 3, entryPoint: 'A', entryNodeId: 'a', nodes, edges: [] };
}

describe('flowDbTables (Fase B, F04)', () => {
  // WEB-UXB-001
  it('junta as tabelas dos nós sem repetir e agrupa o que cada uma sofre', () => {
    const tables = flowDbTables(graphWith(
      { id: 'a', kind: NodeKind.Method, label: 'A', db: [{ target: 'pedidos', operation: DbOperation.Read, line: 1 }, { target: 'estoque', operation: DbOperation.Update, line: 2 }] },
      { id: 'b', kind: NodeKind.Method, label: 'B', db: [{ target: 'Pedidos', operation: DbOperation.Insert, line: 3 }, { target: 'pedidos', operation: DbOperation.Delete, line: 4 }, { target: 'sp_baixar', operation: DbOperation.Execute, line: 5 }] },
      { id: 'c', kind: NodeKind.Method, label: 'C' },
    ));

    expect(tables).toEqual([
      { name: 'estoque', effects: [DbEffect.Write] },
      { name: 'pedidos', effects: [DbEffect.Read, DbEffect.Write] },
      { name: 'sp_baixar', effects: [DbEffect.Execute] },
    ]);
  });

  // WEB-UXB-002
  it('sem acesso a banco não há tabelas', () => {
    expect(flowDbTables(graphWith({ id: 'a', kind: NodeKind.Method, label: 'A' }))).toEqual([]);
  });

  // WEB-UXB-003
  it('com uma tabela só a rota leva ?tabela=; com várias, não leva', () => {
    expect(dbMapQuery([{ name: 'pedidos', effects: [DbEffect.Read] }])).toEqual({ tabela: 'pedidos' });
    expect(dbMapQuery([{ name: 'a', effects: [] }, { name: 'b', effects: [] }])).toBeNull();
    expect(dbMapQuery([])).toBeNull();
  });
});
