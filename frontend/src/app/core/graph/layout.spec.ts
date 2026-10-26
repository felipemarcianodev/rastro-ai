import { NodeKind } from '../models';
import { MAX_NODE_WIDTH, nodeSize } from './layout';

describe('nodeSize', () => {
  it('nome curto fica na largura base do tipo', () => {
    expect(nodeSize({ id: 'a', kind: NodeKind.Method, label: 'Job.Run' }).width).toBe(252);
  });

  it('nome longo alarga o nó para o dono do método caber', () => {
    const node = { id: 'b', kind: NodeKind.Method, label: 'AprovacaoPedidoAppService.RegistrarPagamentoAsync' };

    expect(nodeSize(node).width).toBeGreaterThan(400);
  });

  it('não passa do teto, por maior que seja o nome', () => {
    const node = { id: 'c', kind: NodeKind.Method, label: 'X'.repeat(200) };

    expect(nodeSize(node).width).toBe(MAX_NODE_WIDTH);
    expect(nodeSize(node).height).toBe(56);
  });
});
