import { EdgeConfidence, FlowGraph, FlowMap, MapBlockKind, MapLaneKind, MapLinkKind, NodeKind } from '../models';

// Mapa de teste (Fase 31): o pedido que decide pelo pagamento, estorna por outra API e chama um
// serviço de fora. Dois caminhos: o que estorna e o que para com erro.
export const MAP_FIXTURE: FlowMap = {
  lanes: [
    { id: ':principal', kind: MapLaneKind.Code, system: null },
    { id: 'api-pagamentos', kind: MapLaneKind.Code, system: 'api-pagamentos' },
    { id: ':fora', kind: MapLaneKind.Outside, system: null },
  ],
  blocks: [
    { id: 'b1', kind: MapBlockKind.Entry, lane: ':principal', order: 1, title: 'PedidosController.Criar', nodeId: 'n1', nodeIds: ['n1', 'n2'], location: { file: 'Pedidos.cs', line: 10 } },
    {
      id: 'b2',
      kind: MapBlockKind.Decision,
      lane: ':principal',
      order: 2,
      title: 'pagamento.Aprovado',
      nodeId: 'n3',
      nodeIds: ['n3'],
      condition: 'pagamento.Aprovado',
      outcomes: [
        { label: 'then', to: 'b3', ends: false, fails: false },
        { label: 'else', to: null, ends: true, fails: true },
      ],
    },
    { id: 'b3', kind: MapBlockKind.Step, lane: ':principal', order: 3, title: 'EstornoService.Estornar', nodeId: 'n4', nodeIds: ['n4'] },
    { id: 'b4', kind: MapBlockKind.Entry, lane: 'api-pagamentos', order: 4, title: 'EstornosController.Criar', nodeId: 'n5', nodeIds: ['n5'] },
    { id: 'b5', kind: MapBlockKind.Outside, lane: ':fora', order: 5, title: 'HttpClient.PostAsync', nodeId: 'n6', nodeIds: ['n6'] },
  ],
  links: [
    { from: 'b1', to: 'b2', kind: MapLinkKind.Sequence },
    { from: 'b2', to: 'b3', kind: MapLinkKind.Outcome, label: 'then' },
    { from: 'b3', to: 'b4', kind: MapLinkKind.Call, http: { method: 'POST', route: 'api/estornos' } },
    { from: 'b4', to: 'b5', kind: MapLinkKind.Call },
  ],
  paths: [
    { id: 'p1', blocks: ['b1', 'b2', 'b3', 'b4', 'b5'], choices: [{ decision: 'b2', outcome: 0 }], fails: false },
    { id: 'p2', blocks: ['b1', 'b2'], choices: [{ decision: 'b2', outcome: 1 }], fails: true },
  ],
  pathsTruncated: false,
};

export const MAP_FIXTURE_GRAPH: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'PedidosController.Criar',
  entryNodeId: 'n1',
  nodes: [
    { id: 'n1', kind: NodeKind.Entry, label: 'PedidosController.Criar', location: { file: 'Pedidos.cs', line: 10 } },
    { id: 'n2', kind: NodeKind.Method, label: 'PedidoValidator.ValidarAsync', location: { file: 'Validator.cs', line: 7 } },
    { id: 'n3', kind: NodeKind.Decision, label: 'if (pagamento.Aprovado)', condition: 'pagamento.Aprovado' },
    { id: 'n4', kind: NodeKind.Method, label: 'EstornoService.Estornar' },
    { id: 'n5', kind: NodeKind.Entry, label: 'EstornosController.Criar' },
    { id: 'n6', kind: NodeKind.External, label: 'HttpClient.PostAsync' },
  ],
  edges: [
    { from: 'n1', to: 'n2', order: 0, confidence: EdgeConfidence.Extracted },
    { from: 'n2', to: 'n3', order: 0, confidence: EdgeConfidence.Extracted },
    { from: 'n3', to: 'n4', order: 0, label: 'then', confidence: EdgeConfidence.Extracted },
  ],
};
