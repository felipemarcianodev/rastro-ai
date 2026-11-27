import { BoardDrawioBase, boardDrawioBase, boardDrawioChanges, boardKey, boardShifts } from './drawio-board';

// Dois balões com o mesmo id de passo em faixas diferentes (o id é por levantamento).
const node = (n: number, lane: string, x: number, y: number, label = 'Passo') =>
  `<UserObject id="n${n}" label="${label}" rastroId="p0" rastroNode="m${n}" rastroLane="${lane}"><mxCell style="rounded=1" vertex="1" parent="1"><mxGeometry x="${x}" y="${y}" width="200" height="80" as="geometry"/></mxCell></UserObject>`;
const drawing = (...cells: string[]) => `<mxfile><diagram id="rastro" name="x"><mxGraphModel><root><mxCell id="0"/><mxCell id="1" parent="0"/>${cells.join('')}</root></mxGraphModel></diagram></mxfile>`;
const BOXES = new Map([
  ['a', new Map([['p0', { x: 100, y: 100 }]])],
  ['b', new Map([['p0', { x: 100, y: 100 }]])],
]);
const generated = drawing(node(0, 'a', 40, 40), node(1, 'b', 500, 40));

describe('drawio-board (Fase 68)', () => {
  // WEB-BDRW-010
  it('boardShifts_idsIguaisEmFaixasDiferentes_daUmaDiferencaParaCadaFaixa', () => {
    const shifts = boardShifts(generated, BOXES);

    expect(shifts.get('a')).toEqual({ x: -60, y: -60 });
    expect(shifts.get('b')).toEqual({ x: 400, y: -60 });
  });

  // WEB-BDRW-011
  it('boardDrawioChanges_moverUmBalao_voltaACoordenadaDaFaixaSemConfundirOutraFaixa', () => {
    const base: BoardDrawioBase = boardDrawioBase(generated, generated, BOXES);
    const moved = drawing(node(0, 'a', 40, 40), node(1, 'b', 540, 40));

    const changes = boardDrawioChanges(moved, base)!;

    expect(changes.moved.map((m) => m.step)).toEqual([{ laneSurveyId: 'b', stepId: 'p0', x: 140, y: 100 }]);
    expect(changes.labels).toEqual([]);
    expect(changes.annotationsChanged).toBeFalse();
    expect(base.positions.get(boardKey('a', 'p0'))).toEqual({ x: 40, y: 40 });
  });

  // WEB-BDRW-012
  it('boardDrawioChanges_textoEAnotacao_apareceNoQueMudou', () => {
    const base = boardDrawioBase(generated, generated, BOXES);
    const note = '<mxCell id="x1" value="Nota" vertex="1" parent="1"><mxGeometry x="1" y="1" width="10" height="10" as="geometry"/></mxCell>';

    const changes = boardDrawioChanges(drawing(node(0, 'a', 40, 40, 'Pedido novo'), node(1, 'b', 500, 40), note), base)!;

    expect(changes.labels).toEqual([{ lane: 'a', stepId: 'p0', text: 'Pedido novo' }]);
    expect(changes.annotationsChanged).toBeTrue();
    expect(changes.annotations).toContain('id="x1"');
  });

  // WEB-BDRW-013
  it('boardDrawioChanges_xmlIlegivel_naoMudaNada', () => {
    expect(boardDrawioChanges('<mxfile>comprimido</mxfile>', boardDrawioBase(generated, generated, BOXES))).toBeNull();
  });
});
