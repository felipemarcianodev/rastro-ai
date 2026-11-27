import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EditorLinks, isVsCodeLink, vscodeLink } from './editor-links';
import { RepositoryHostKind, RepositoryItem } from './models';

describe('vscodeLink', () => {
  it('junta a pasta do Windows com o arquivo do repositório e a linha', () => {
    expect(vscodeLink('C:\\codigo\\meu-sistema\\', 'backend/src/Jobs/Pedido Job.cs', 42)).toBe('vscode://file/C:/codigo/meu-sistema/backend/src/Jobs/Pedido%20Job.cs:42');
  });

  it('aceita pasta do Linux ou do Mac', () => {
    expect(vscodeLink('/home/eu/meu-sistema', 'src/App.cs', 7)).toBe('vscode://file/home/eu/meu-sistema/src/App.cs:7');
  });

  it('sem pasta, com linha inválida ou subindo de pasta não monta link', () => {
    expect(vscodeLink('', 'src/App.cs', 7)).toBeNull();
    expect(vscodeLink('C:/p', 'src/App.cs', 0)).toBeNull();
    expect(vscodeLink('C:/p', '../segredo.txt', 1)).toBeNull();
    expect(vscodeLink('projetos/meu-sistema', 'src/App.cs', 1)).toBeNull();
  });
});

describe('EditorLinks', () => {
  const repo: RepositoryItem = { name: 'meu-sistema', path: 'C:\\codigo\\meu-sistema', hasGit: true, hasBuildCommand: false, hasTestCommand: false };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  });

  it('carrega a lista da API uma vez e monta o link pela pasta cadastrada', () => {
    const editor = TestBed.inject(EditorLinks);
    const http = TestBed.inject(HttpTestingController);
    editor.ensureLoaded();
    editor.ensureLoaded();
    http.expectOne('/api/repositories').flush([repo]);

    expect(editor.link('meu-sistema', 'src/App.cs', 3)).toBe('vscode://file/C:/codigo/meu-sistema/src/App.cs:3');
    expect(editor.link('outro', 'src/App.cs', 3)).toBeNull();
  });

  it('set() troca a lista depois de adicionar ou remover', () => {
    const editor = TestBed.inject(EditorLinks);
    editor.set([repo]);
    expect(editor.folder('meu-sistema')).toBe(repo.path);
    editor.set([]);
    expect(editor.link('meu-sistema', 'src/App.cs', 3)).toBeNull();
  });

  // Pendência da Fase D: o grafo segue o editor configurado (Rastro:Editor:UrlTemplate), como a visão de negócio.
  it('comOModeloDoLevantamento_usaOEditorConfigurado_eAoLimparVoltaAoVsCode', () => {
    const editor = TestBed.inject(EditorLinks);
    editor.set([repo]);
    editor.useSurveyLinks([
      { repository: 'Meu-Sistema', host: RepositoryHostKind.Unknown, commitTemplate: null, branchTemplate: null, editorTemplate: 'jetbrains://open?file=C:/codigo/meu-sistema/{path}&line={line}' },
    ]);

    const link = editor.link('meu-sistema', 'src/Pedido Job.cs', 7);
    expect(link).toBe('jetbrains://open?file=C:/codigo/meu-sistema/src/Pedido%20Job.cs&line=7');
    expect(isVsCodeLink(link)).toBeFalse();

    editor.clearSurveyLinks();
    expect(isVsCodeLink(editor.link('meu-sistema', 'src/App.cs', 3))).toBeTrue();
  });
});
