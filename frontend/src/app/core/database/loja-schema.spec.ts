import { DbColumn, DbSchema, DbTable, DeleteAction, SchemaFindingKind, SchemaSourceKind, SqlDialect } from './db-schema';

// A loja fictícia dos testes do backend (Fixtures/LojaBanco), no formato que a API devolve.
const model = { kind: SchemaSourceKind.EfModel, file: 'Loja.Api/Dados/LojaDbContext.cs', line: 7 };
const migration = { kind: SchemaSourceKind.EfMigration, file: 'Loja.Api/Migrations/20261012_Pedidos.cs', line: 9 };
const sql = { kind: SchemaSourceKind.SqlScript, file: 'banco/produtos.sql', line: 2 };

function col(name: string, type: string | null, extra: Partial<DbColumn> = {}): DbColumn {
  return { name, type, nullable: false, default: null, identity: false, sources: [model], personal: false, ...extra };
}

function table(name: string, columns: DbColumn[], pk: string[], fks: [string, string][], extra: Partial<DbTable> = {}): DbTable {
  return {
    schema: null,
    name,
    columns,
    primaryKey: pk.length ? { name: `PK_${name}`, columns: pk, source: migration } : null,
    foreignKeys: fks.map(([column, target]) => ({ name: null, columns: [column], principalTable: target, principalColumns: ['id'], onDelete: DeleteAction.Cascade, source: migration })),
    indexes: [],
    sources: [model, migration],
    ...extra,
  };
}

export function lojaSchema(): DbSchema {
  return {
    dialect: SqlDialect.SqlServer,
    tables: [
      table('clientes', [col('id', 'int', { identity: true }), col('nome', 'nvarchar(120)'), col('email', 'nvarchar(200)', { personal: true }), col('cpf', 'char(11)', { nullable: true, personal: true })], ['id'], [], {
        indexes: [{ name: 'UX_clientes_email', columns: ['email'], included: [], unique: true, source: migration, convention: false }],
      }),
      table('itens_pedido', [col('pedido_id', 'int'), col('produto_id', 'int'), col('quantidade', 'int')], ['pedido_id', 'produto_id'], [
        ['pedido_id', 'pedidos'],
        ['produto_id', 'produtos'],
      ]),
      table('log_eventos', [col('ocorrido_em', 'datetime2')], [], [], { sources: [sql] }),
      table('pagamentos', [col('id', 'int'), col('pedido_id', 'int'), col('valor', 'decimal(10,2)')], ['id'], [['pedido_id', 'pedidos']]),
      table('pedidos', [col('id', 'int'), col('cliente_id', 'int'), col('status', null)], ['id'], [['cliente_id', 'clientes']], {
        indexes: [{ name: 'IX_pedidos_cliente_id', columns: ['cliente_id'], included: [], unique: false, source: migration, convention: false }],
      }),
      table('produtos', [col('id', 'int'), col('nome', 'nvarchar(160)')], ['id'], [], { sources: [sql] }),
    ],
    views: [{ schema: null, name: 'vw_pedidos_por_cliente', columns: ['cliente_id', 'nome', 'pedidos'], reads: ['dbo.clientes', 'dbo.pedidos'], source: { kind: SchemaSourceKind.SqlScript, file: 'banco/views.sql', line: 1 } }],
    procedures: [
      {
        schema: null,
        name: 'sp_baixar_estoque',
        parameters: [{ name: '@pedido_id', type: 'int', output: false }],
        reads: ['dbo.produtos', 'dbo.itens_pedido'],
        writes: ['dbo.produtos', 'dbo.log_eventos'],
        source: { kind: SchemaSourceKind.SqlScript, file: 'banco/sp_baixar_estoque.sql', line: 1 },
      },
    ],
    divergences: [
      {
        kind: 0,
        table: 'pedidos',
        column: 'status',
        values: [
          { value: 'nvarchar(20)', source: model },
          { value: 'nvarchar(30)', source: migration },
        ],
      },
    ],
    notes: [{ message: 'O arquivo não pôde ser lido.', file: 'banco/quebrado.sql', line: 1 }],
    findings: [
      { kind: SchemaFindingKind.ForeignKeyWithoutIndex, table: 'itens_pedido', columns: ['produto_id'], source: sql },
      { kind: SchemaFindingKind.ForeignKeyWithoutIndex, table: 'pagamentos', columns: ['pedido_id'], source: { kind: SchemaSourceKind.EfMigration, file: 'Loja.Api/Migrations/20261020_Pagamentos.cs', line: 24 } },
    ],
  };
}

// A loja com pedidos de 18 campos, com a FK depois dos 12 primeiros.
export function wideSchema(): DbSchema {
  const schema = lojaSchema();
  const pedidos = schema.tables.find((t) => t.name === 'pedidos')!;
  const [id, clienteId, status] = pedidos.columns;
  const filler = Array.from({ length: 14 }, (_, i) => ({ ...status, name: `campo_${String(i + 1).padStart(2, '0')}`, type: 'int' }));
  pedidos.columns = [id, status, ...filler, clienteId, { ...status, name: 'atualizado_em', type: 'datetime2' }];
  return schema;
}
