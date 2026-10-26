import { EFConnectableSide } from '@foblex/flow';
import { TreeConnectionBuilder, treePoints } from './tree-connection';

describe('ligação da árvore', () => {
  it('tronco: desce da porta de baixo e vira para o passo', () => {
    expect(treePoints({ x: 20, y: 56 }, EFConnectableSide.BOTTOM, { x: 40, y: 100 })).toEqual([
      { x: 20, y: 56 },
      { x: 20, y: 100 },
      { x: 40, y: 100 },
    ]);
  });

  it('galho na mesma linha: reta para a direita, sem dobra', () => {
    expect(treePoints({ x: 236, y: 22 }, EFConnectableSide.RIGHT, { x: 332, y: 22 })).toEqual([
      { x: 236, y: 22 },
      { x: 284, y: 22 },
      { x: 332, y: 22 },
    ]);
  });

  it('galho de baixo: sai para a direita, desce no meio do vão e entra pela esquerda', () => {
    const points = treePoints({ x: 236, y: 22 }, EFConnectableSide.RIGHT, { x: 332, y: 90 });

    expect(points).toEqual([
      { x: 236, y: 22 },
      { x: 284, y: 22 },
      { x: 284, y: 90 },
      { x: 332, y: 90 },
    ]);
  });

  it('gera o caminho com cantos arredondados e termina no passo', () => {
    const result = new TreeConnectionBuilder().handle({
      source: { x: 20, y: 56 },
      sourceSide: EFConnectableSide.BOTTOM,
      target: { x: 40, y: 100 },
      targetSide: EFConnectableSide.LEFT,
      radius: 8,
      offset: 0,
      waypoints: [],
    });

    expect(result.path.startsWith('M 20 56')).toBeTrue();
    expect(result.path).toContain('Q 20 100');
    expect(result.path.endsWith('L 40 100')).toBeTrue();
    expect(result.penultimatePoint).toEqual({ x: 20, y: 100 });
  });
});
