import { RepositoryHostKind, RepositoryLinks } from '../models';
import { fillLinkTemplate, hostLinkResolver } from './link-template';

// Fase 56: a tela só preenche o modelo de link que a API monta (CodeLinks no backend).

const links: RepositoryLinks[] = [
  {
    repository: 'api-pedidos',
    host: RepositoryHostKind.GitHub,
    commitTemplate: 'https://github.com/loja/api-pedidos/blob/abc123/{path}#L{line}',
    branchTemplate: 'https://github.com/loja/api-pedidos/blob/main/{path}#L{line}',
    editorTemplate: 'vscode://file/C:/loja/{path}:{line}',
  },
  { repository: 'api-pagamentos', host: RepositoryHostKind.Unknown, commitTemplate: null, branchTemplate: null, editorTemplate: 'vscode://file/C:/pag/{path}:{line}' },
];

describe('link-template', () => {
  it('preenche o caminho codificado por segmento e a linha, como o servidor', () => {
    expect(fillLinkTemplate('https://h/{path}#L{line}', 'src/Serviços de Pedido/A.cs', 7)).toBe('https://h/src/Servi%C3%A7os%20de%20Pedido/A.cs#L7');
    expect(fillLinkTemplate('vscode://file/C:/x/{path}:{line}', 'src\\a.cs', 3)).toBe('vscode://file/C:/x/src/a.cs:3');
  });

  it('resolve o link pelo repositório (sem diferenciar maiúsculas) e só com remote reconhecido e local', () => {
    const linkOf = hostLinkResolver(links);

    expect(linkOf('API-Pedidos', { file: 'src/a.cs', line: 9 })).toBe('https://github.com/loja/api-pedidos/blob/abc123/src/a.cs#L9');
    expect(linkOf('api-pagamentos', { file: 'src/a.cs', line: 9 })).toBeNull();
    expect(linkOf('api-pedidos', null)).toBeNull();
    expect(linkOf('outro', { file: 'a.cs', line: 1 })).toBeNull();
  });
});
