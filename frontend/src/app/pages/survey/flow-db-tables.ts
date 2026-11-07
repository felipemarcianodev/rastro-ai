import { DbOperation, FlowGraph } from '../../core/models';

// O que o fluxo faz com uma tabela, em três grupos (gravar junta incluir, alterar e excluir).
export enum DbEffect {
  Read = 0,
  Write = 1,
  Execute = 2,
}

export const DB_EFFECT_LABEL: Record<DbEffect, string> = {
  [DbEffect.Read]: 'lê',
  [DbEffect.Write]: 'grava',
  [DbEffect.Execute]: 'executa',
};

export const DB_EFFECT_SLUG: Record<DbEffect, string> = {
  [DbEffect.Read]: 'read',
  [DbEffect.Write]: 'insert',
  [DbEffect.Execute]: 'execute',
};

const EFFECT_OF_OPERATION: Record<DbOperation, DbEffect> = {
  [DbOperation.Read]: DbEffect.Read,
  [DbOperation.Insert]: DbEffect.Write,
  [DbOperation.Update]: DbEffect.Write,
  [DbOperation.Delete]: DbEffect.Write,
  [DbOperation.Execute]: DbEffect.Execute,
};

export interface FlowDbTable {
  name: string;
  // Na ordem de DbEffect, sem repetir.
  effects: DbEffect[];
}

// Tabelas que o fluxo toca, sem repetição (o nome não diferencia maiúscula), com o que cada uma sofre.
export function flowDbTables(graph: FlowGraph): FlowDbTable[] {
  const found = new Map<string, { name: string; effects: Set<DbEffect> }>();
  for (const node of graph.nodes) {
    for (const access of node.db ?? []) {
      const key = access.target.toLowerCase();
      const entry = found.get(key) ?? { name: access.target, effects: new Set<DbEffect>() };
      entry.effects.add(EFFECT_OF_OPERATION[access.operation]);
      found.set(key, entry);
    }
  }
  return [...found.values()]
    .map((entry) => ({ name: entry.name, effects: [...entry.effects].sort((a, b) => a - b) }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

// Rota do mapa do banco: com uma tabela só, já abre nela.
export function dbMapQuery(tables: FlowDbTable[]): { tabela: string } | null {
  return tables.length === 1 ? { tabela: tables[0].name } : null;
}
