import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EditorLinks, vscodeLink } from './editor-links';
import { RepositoryItem } from './models';

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
});
