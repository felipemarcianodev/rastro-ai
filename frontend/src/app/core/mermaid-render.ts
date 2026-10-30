import { InjectionToken } from '@angular/core';

// Renderização do Mermaid (Fase 27). A biblioteca é grande: entra só por import() dinâmico, na
// tela da visão consolidada, num chunk à parte. Nada de CDN.

export interface DiagramColors {
  paper: string;
  surface: string;
  surfaceSunk: string;
  ink: string;
  inkSoft: string;
  lineStrong: string;
  edge: string;
}

// Papel claro, usado quando o tema não entrega uma cor em hexadecimal.
const FALLBACK_COLORS: DiagramColors = {
  paper: '#f2f4f1',
  surface: '#ffffff',
  surfaceSunk: '#e8ece9',
  ink: '#1d2a2e',
  inkSoft: '#5c6a6e',
  lineStrong: '#b7c1bc',
  edge: '#6b787c',
};

const TOKEN: Record<keyof DiagramColors, string> = {
  paper: '--paper',
  surface: '--surface',
  surfaceSunk: '--surface-sunk',
  ink: '--ink',
  inkSoft: '--ink-soft',
  lineStrong: '--line-strong',
  edge: '--edge',
};

const FONT = "'Instrument Sans', 'Segoe UI', system-ui, sans-serif";
const HEX6 = /^#[0-9a-fA-F]{6}$/;

// As cores do tema ativo (paleta e claro/escuro). O tema "base" do Mermaid só aceita hexadecimal.
export function readDiagramColors(): DiagramColors {
  const style = getComputedStyle(document.documentElement);
  const read = (key: keyof DiagramColors) => {
    const value = style.getPropertyValue(TOKEN[key]).trim();
    return HEX6.test(value) ? value : FALLBACK_COLORS[key];
  };
  return {
    paper: read('paper'),
    surface: read('surface'),
    surfaceSunk: read('surfaceSunk'),
    ink: read('ink'),
    inkSoft: read('inkSoft'),
    lineStrong: read('lineStrong'),
    edge: read('edge'),
  };
}

// O texto do diagrama é editável por quem usa a tela. O modo "strict" do Mermaid recusa
// HTML e "click" e passa o SVG pelo DOMPurify; por isso nunca se baixa esse nível, e os rótulos
// ficam em SVG puro (htmlLabels: false).
export function mermaidConfig(colors: DiagramColors) {
  return {
    startOnLoad: false,
    securityLevel: 'strict' as const,
    theme: 'base' as const,
    suppressErrorRendering: true,
    // O backend aceita até 200000 caracteres; o padrão do Mermaid (50000) cortaria antes.
    maxTextSize: 200_000,
    maxEdges: 2_000,
    flowchart: { htmlLabels: false, curve: 'basis' as const },
    themeVariables: {
      fontFamily: FONT,
      background: colors.paper,
      primaryColor: colors.surface,
      primaryTextColor: colors.ink,
      primaryBorderColor: colors.lineStrong,
      secondaryColor: colors.surfaceSunk,
      tertiaryColor: colors.surfaceSunk,
      lineColor: colors.edge,
      textColor: colors.ink,
      mainBkg: colors.surface,
      nodeBorder: colors.lineStrong,
      edgeLabelBackground: colors.paper,
    },
  };
}

const SVG_NS = 'http://www.w3.org/2000/svg';

// O SVG da prévia tem largura 100% e fundo transparente; para arquivo e PNG ele ganha o tamanho
// do viewBox e um fundo da cor do papel. Devolve null se o SVG não for um XML válido.
export function standaloneSvg(svg: string, background: string): { svg: string; width: number; height: number } | null {
  const doc = new DOMParser().parseFromString(svg, 'image/svg+xml');
  const root = doc.documentElement;
  if (root.nodeName !== 'svg' || doc.querySelector('parsererror')) return null;
  const box = root.getAttribute('viewBox')?.split(/[\s,]+/).map(Number);
  if (!box || box.length !== 4 || box.some((n) => Number.isNaN(n))) return null;
  const [x, y, width, height] = box;
  root.setAttribute('width', String(Math.ceil(width)));
  root.setAttribute('height', String(Math.ceil(height)));
  root.removeAttribute('style');
  const rect = doc.createElementNS(SVG_NS, 'rect');
  rect.setAttribute('x', String(x));
  rect.setAttribute('y', String(y));
  rect.setAttribute('width', String(width));
  rect.setAttribute('height', String(height));
  rect.setAttribute('fill', background);
  root.insertBefore(rect, root.firstChild);
  return { svg: new XMLSerializer().serializeToString(root), width: Math.ceil(width), height: Math.ceil(height) };
}

export type DiagramRenderer = (text: string, colors: DiagramColors) => Promise<string>;

let counter = 0;

export const renderMermaid: DiagramRenderer = async (text, colors) => {
  const { default: mermaid } = await import('mermaid');
  mermaid.initialize(mermaidConfig(colors));
  const id = `rastro-diagram-${++counter}`;
  try {
    return (await mermaid.render(id, text)).svg;
  } finally {
    // Numa falha o Mermaid pode deixar o elemento de trabalho solto no body.
    document.getElementById(`d${id}`)?.remove();
    document.getElementById(id)?.remove();
  }
};

// Trocável nos testes da tela (o Mermaid de verdade é lento e testado à parte).
export const DIAGRAM_RENDERER = new InjectionToken<DiagramRenderer>('DIAGRAM_RENDERER', { providedIn: 'root', factory: () => renderMermaid });
