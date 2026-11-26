import { RepositoryLinks, SourceLocation } from '../models';

// Links do código (Fase 56): a API devolve, por repositório, o modelo de link com {path} e {line}. A regra de cada
// host fica só no backend (CodeLinks); aqui só se preenche o modelo, como o CodeLinks.Fill do servidor.

export const LINK_PATH_TOKEN = '{path}';
export const LINK_LINE_TOKEN = '{line}';

// Cada segmento do caminho vai codificado (espaço, acento) e a barra fica.
export function fillLinkTemplate(template: string, path: string, line: number): string {
  const encoded = path
    .replaceAll('\\', '/')
    .split('/')
    .filter((segment) => segment.length > 0)
    .map(encodeURIComponent)
    .join('/');
  return template.split(LINK_PATH_TOKEN).join(encoded).split(LINK_LINE_TOKEN).join(String(line));
}

// Link para o host pelo commit do levantamento; nulo sem remote reconhecido ou sem local no código.
export type HostLinkOf = (repository: string, location: SourceLocation | null | undefined) => string | null;

export function hostLinkResolver(links: readonly RepositoryLinks[]): HostLinkOf {
  const templates = new Map(links.filter((l) => l.commitTemplate).map((l) => [l.repository.toLowerCase(), l.commitTemplate!]));
  return (repository, location) => {
    const template = templates.get(repository.toLowerCase());
    return template && location ? fillLinkTemplate(template, location.file, location.line) : null;
  };
}
