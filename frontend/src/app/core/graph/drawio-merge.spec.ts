import { applyDrawioLayout, drawioBaseline, drawioLabelText, drawioShift, readDrawioChanges } from './drawio-merge';

// Um .drawio como o toDrawio gera: moldura, dois balões do código, um repetido (sem rastroId) e uma ligação.
const GENERATED =
  '<?xml version="1.0" encoding="UTF-8"?>\n<mxfile host="Rastro" type="device"><diagram id="rastro" name="x"><mxGraphModel><root>' +
  '<mxCell id="0"/><mxCell id="1" parent="0"/>' +
  '<mxCell id="f0" value="Sistema" style="dashed=1" vertex="1" parent="1"><mxGeometry x="10" y="10" width="500" height="300" as="geometry"/></mxCell>' +
  '<UserObject id="n0" label="Pedido recebido&lt;br&gt;&lt;span style=&quot;font-size:9px;font-family:Consolas,monospace&quot;&gt;Loja · Pedido.cs:10&lt;/span&gt;" rastroId="b1" rastroNode="entry"><mxCell style="rounded=1" vertex="1" parent="1"><mxGeometry x="40" y="40" width="220" height="88" as="geometry"/></mxCell></UserObject>' +
  '<UserObject id="n1" label="O pedido é válido?" rastroId="b2" rastroNode="m0"><mxCell style="rhombus" vertex="1" parent="1"><mxGeometry x="40" y="200" width="240" height="110" as="geometry"/></mxCell></UserObject>' +
  '<UserObject id="n2" label="↻ Repetido" rastroNode="m0"><mxCell style="rounded=1" vertex="1" parent="1"><mxGeometry x="400" y="40" width="100" height="60" as="geometry"/></mxCell></UserObject>' +
  '<mxCell id="e0" value="" style="edgeStyle=orthogonalEdgeStyle" edge="1" parent="1" source="n0" target="n1"><mxGeometry relative="1" as="geometry"/></mxCell>' +
  '</root></mxGraphModel></diagram></mxfile>\n';

const NOTE = '<mxCell id="abc-1" value="Atenção aqui" style="text;html=1" vertex="1" parent="1"><mxGeometry x="300" y="120" width="120" height="30" as="geometry"/></mxCell>';

function geometryOf(xml: string, id: string): Element {
  const doc = new DOMParser().parseFromString(xml, 'application/xml');
  const cell = Array.from(doc.getElementsByTagName('root')[0].children).find((c) => c.getAttribute('id') === id)!;
  return cell.getElementsByTagName('mxGeometry')[0];
}

describe('drawio-merge (Fase 65)', () => {
  // WEB-DRMG-001
  it('applyDrawioLayout_trocaAGeometriaDoBalaoComRastroId_eDeixaOResto', () => {
    const merged = applyDrawioLayout(GENERATED, { cells: [{ rastroId: 'b2', x: 500, y: 600, width: 260, height: 120 }], annotations: null });

    const moved = geometryOf(merged, 'n1');
    expect([moved.getAttribute('x'), moved.getAttribute('y'), moved.getAttribute('width'), moved.getAttribute('height')]).toEqual(['500', '600', '260', '120']);
    const kept = geometryOf(merged, 'n0');
    expect([kept.getAttribute('x'), kept.getAttribute('y')]).toEqual(['40', '40']);
    expect(merged).toContain('rastroNode="m0"');
  });

  // WEB-DRMG-002
  it('applyDrawioLayout_balaoQueSumiuDoFluxo_eIgnorado', () => {
    const merged = applyDrawioLayout(GENERATED, { cells: [{ rastroId: 'fantasma', x: 1, y: 2, width: 3, height: 4 }], annotations: null });

    expect(readDrawioChanges(merged)!.cells.map((c) => c.rastroId)).toEqual(['b1', 'b2']);
    expect(readDrawioChanges(merged)!.cells[0].x).toBe(40);
  });

  // WEB-DRMG-003
  it('applyDrawioLayout_acrescentaAsAnotacoesAntesDoFimDaRaiz_semDuplicar', () => {
    const annotations = `<root>${NOTE}</root>`;
    const merged = applyDrawioLayout(GENERATED, { cells: [], annotations });
    const again = applyDrawioLayout(merged, { cells: [], annotations });

    const ids = (xml: string) => Array.from(new DOMParser().parseFromString(xml, 'application/xml').getElementsByTagName('root')[0].children).map((c) => c.getAttribute('id'));
    expect(ids(merged)).toEqual(['0', '1', 'f0', 'n0', 'n1', 'n2', 'e0', 'abc-1']);
    expect(ids(again)).toEqual(ids(merged));
  });

  // WEB-DRMG-004
  it('applyDrawioLayout_semNadaGuardado_ouComXmlTorto_devolveOXmlComoVeio', () => {
    expect(applyDrawioLayout(GENERATED, { cells: [], annotations: null })).toBe(GENERATED);
    expect(applyDrawioLayout('<mxfile>', { cells: [{ rastroId: 'b1', x: 1, y: 1, width: 1, height: 1 }], annotations: null })).toBe('<mxfile>');
  });

  // WEB-DRMG-005
  it('applyDrawioLayout_ignoraAnotacaoComIdDeCelulaGerada', () => {
    const merged = applyDrawioLayout(GENERATED, { cells: [], annotations: '<root><mxCell id="n0" value="intruso" vertex="1" parent="1"/><mxCell id="e0" value="x" edge="1" parent="1"/></root>' });

    expect(merged).not.toContain('intruso');
  });

  // WEB-DRMG-006
  it('readDrawioChanges_leAsPosicoesDosBaloesEOTextoDoPasso', () => {
    const changes = readDrawioChanges(GENERATED)!;

    expect(changes.cells).toEqual([
      { rastroId: 'b1', x: 40, y: 40, width: 220, height: 88 },
      { rastroId: 'b2', x: 40, y: 200, width: 240, height: 110 },
    ]);
    expect(changes.texts.get('b1')).toBe('Pedido recebido');
    expect(changes.texts.get('b2')).toBe('O pedido é válido?');
    expect(changes.annotations).toBeNull();
  });

  // WEB-DRMG-007
  it('readDrawioChanges_soGuardaComoAnotacaoACelulaDesenhadaNoEditor', () => {
    const drawn = GENERATED.replace('</root>', `${NOTE}<UserObject id="xyz-2" label="Com link" link="https://exemplo.com"><mxCell style="rounded=1" vertex="1" parent="1"><mxGeometry x="5" y="6" width="70" height="40" as="geometry"/></mxCell></UserObject></root>`);

    const annotations = readDrawioChanges(drawn)!.annotations!;

    expect(annotations.startsWith('<root>')).toBeTrue();
    expect(annotations.endsWith('</root>')).toBeTrue();
    const doc = new DOMParser().parseFromString(annotations, 'application/xml');
    const cells = Array.from(doc.getElementsByTagName('root')[0].children);
    expect(cells.map((c) => c.localName)).toEqual(['mxCell', 'mxCell']);
    expect(cells.map((c) => c.getAttribute('id'))).toEqual(['abc-1', 'xyz-2']);
    expect(cells[1].getAttribute('value')).toBe('Com link');
    expect(cells[1].getElementsByTagName('mxGeometry')[0].getAttribute('x')).toBe('5');
    expect(annotations).not.toContain('rastroId');
    expect(annotations).not.toContain('f0');
    expect(annotations).not.toContain('Repetido');
  });

  // WEB-DRMG-008
  it('readDrawioChanges_xmlQueNaoDaParaLer_devolveNulo', () => {
    expect(readDrawioChanges('<mxfile><diagram>eJzLSM3JyVcozy/KSQEAGgsEXQ==</diagram></mxfile>')).toBeNull();
    expect(readDrawioChanges('isto não é xml')).toBeNull();
  });

  // WEB-DRMG-009
  it('readDrawioChanges_balaoComGeometriaInvalida_naoEntraNasPosicoes', () => {
    const broken = GENERATED.replace('width="220"', 'width="0"');

    const changes = readDrawioChanges(broken)!;
    expect(changes.cells.map((c) => c.rastroId)).toEqual(['b2']);
    // O balão continua existindo (o texto o mostra), então não conta como apagado.
    expect(changes.texts.has('b1')).toBeTrue();
  });

  // WEB-DRMG-010
  it('aplicarELer_dao_oMesmoGuardado', () => {
    const cells = [{ rastroId: 'b1', x: 120.5, y: 80, width: 220, height: 88 }];
    const merged = applyDrawioLayout(GENERATED, { cells, annotations: `<root>${NOTE}</root>` });

    const changes = readDrawioChanges(merged)!;
    expect(changes.cells[0]).toEqual(cells[0]);
    expect(changes.annotations).toContain('Atenção aqui');
  });

  // WEB-DRMG-011
  it('drawioLabelText_tiraAReferenciaDoCodigoEOsEspacos', () => {
    expect(drawioLabelText('Pedido<br><span style="font-family:Consolas,monospace">Loja · a.cs:1</span>')).toBe('Pedido');
    expect(drawioLabelText('Linha um<br>linha dois')).toBe('Linha um\nlinha dois');
    expect(drawioLabelText('Fica &amp; vale&nbsp;&lt;3')).toBe('Fica & vale <3');
    expect(drawioLabelText('<span style="color:red">colorido</span>')).toBe('colorido');
  });

  // WEB-DRMG-012
  it('drawioBaseline_idsETextosVemDoGerado_eAPosicaoDoCarregado', () => {
    const loaded = applyDrawioLayout(GENERATED, { cells: [{ rastroId: 'b1', x: 90, y: 95, width: 220, height: 88 }], annotations: null });

    const baseline = drawioBaseline(GENERATED, loaded, { x: 40, y: 40 });

    expect([...baseline.ids]).toEqual(['b1', 'b2']);
    expect(baseline.texts.get('b2')).toBe('O pedido é válido?');
    expect(baseline.positions.get('b1')).toEqual({ x: 90, y: 95 });
    expect(baseline.shift).toEqual({ x: 40, y: 40 });
  });

  // WEB-DRMG-013
  it('drawioShift_eADiferencaEntreOXmlGeradoEAGeometriaDaTela', () => {
    const boxes = new Map([['b1', { x: 0, y: 10 }], ['b2', { x: 0, y: 170 }]]);

    expect(drawioShift(GENERATED, boxes)).toEqual({ x: 40, y: 30 });
    expect(drawioShift(GENERATED, new Map())).toBeNull();
  });
});
