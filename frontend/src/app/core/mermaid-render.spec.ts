import { DiagramColors, mermaidConfig, readDiagramColors, renderMermaid, standaloneSvg } from './mermaid-render';

const colors: DiagramColors = readDiagramColors();

describe('renderização do Mermaid (Fase 27)', () => {
  // WEB-DIA-001
  it('mermaidConfig_sempre_usaModoStrictESemHtmlNosRotulos', () => {
    const config = mermaidConfig(colors);

    expect(config.securityLevel).toBe('strict');
    expect(config.flowchart.htmlLabels).toBeFalse();
    expect(config.startOnLoad).toBeFalse();
  });

  // WEB-DIA-002
  it('readDiagramColors_semTokensNoTema_usaAsCoresDoPapel', () => {
    const read = readDiagramColors();

    for (const value of Object.values(read)) expect(value).toMatch(/^#[0-9a-fA-F]{6}$/);
  });

  // WEB-DIA-003
  it('renderMermaid_rotuloComScriptEClick_naoExecutaENemDeixaOScriptNoSvg', async () => {
    const w = window as unknown as { __pwned?: boolean };
    w.__pwned = undefined;
    const text = [
      'flowchart TD',
      '  a["#lt;script#gt;window.__pwned = true#lt;/script#gt;"]',
      '  b["<img src=x onerror=window.__pwned=true>"]',
      '  a --> b',
      '  click a call alert(1)',
      '  click b "javascript:window.__pwned=true"',
    ].join('\n');

    let svg = '';
    try {
      svg = await renderMermaid(text, colors);
    } catch {
      // O modo strict pode recusar o "click" inteiro: o que importa é nada executar.
    }
    const holder = document.createElement('div');
    holder.innerHTML = svg;
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(w.__pwned).toBeUndefined();
    expect(holder.querySelector('script')).toBeNull();
    expect(holder.querySelector('[onerror], [onclick]')).toBeNull();
    expect(svg).not.toContain('javascript:');
  });

  // WEB-DIA-004
  it('renderMermaid_fluxoSimples_devolveUmSvgComOsNos', async () => {
    const svg = await renderMermaid('flowchart TD\n  sa["Início"]\n  sb{"Se x"}\n  sa --> sb\n', colors);
    const holder = document.createElement('div');
    holder.innerHTML = svg;

    expect(holder.querySelector('svg')).not.toBeNull();
    expect(holder.querySelectorAll('g.node').length).toBe(2);
    expect(holder.querySelector('g.node')!.id).toMatch(/flowchart-sa-\d+$/);
  });

  // WEB-DIA-005
  it('renderMermaid_sintaxeInvalida_rejeitaComMensagem', async () => {
    await expectAsync(renderMermaid('flowchart TD\n  sa["aberto\n', colors)).toBeRejected();
  });

  // WEB-DIA-006
  it('standaloneSvg_svgDoMermaid_ganhaTamanhoEFundo', () => {
    const image = standaloneSvg('<svg xmlns="http://www.w3.org/2000/svg" width="100%" style="max-width: 120px" viewBox="0 0 120 80"><g/></svg>', '#f2f4f1');

    expect(image).not.toBeNull();
    expect(image!.width).toBe(120);
    expect(image!.height).toBe(80);
    expect(image!.svg).toContain('width="120"');
    expect(image!.svg).toContain('fill="#f2f4f1"');
    expect(image!.svg).not.toContain('max-width');
  });

  // WEB-DIA-007
  it('standaloneSvg_textoQueNaoEXml_devolveNull', () => {
    expect(standaloneSvg('<svg><g></svg>', '#fff')).toBeNull();
    expect(standaloneSvg('nada', '#fff')).toBeNull();
  });
});
