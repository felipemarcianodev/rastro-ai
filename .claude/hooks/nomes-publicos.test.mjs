// Testes do hook nomes-publicos. Rodar da raiz: node --test .claude/hooks/
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, before, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const HOOK = join(dirname(fileURLToPath(import.meta.url)), 'nomes-publicos.mjs');
const NOREPLY = '1+dono@users.noreply.github.com';
let base;
let repo;
let listas;

const git = (...args) => execFileSync('git', ['-C', repo, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });

function rodar(comando, extra = {}) {
  const entrada = JSON.stringify({ tool_name: 'Bash', cwd: repo, tool_input: { command: comando }, ...extra });
  const saida = spawnSync(process.execPath, [HOOK], {
    input: entrada,
    encoding: 'utf8',
    env: { ...process.env, CLAUDE_PROJECT_DIR: repo, RASTRO_PUBLICO_DIR: listas },
  });
  return saida.stdout ? JSON.parse(saida.stdout).hookSpecificOutput.permissionDecisionReason : null;
}

function prepararStage(arquivo, texto) {
  writeFileSync(join(repo, arquivo), texto);
  git('add', arquivo);
}

before(() => {
  base = mkdtempSync(join(tmpdir(), 'nomes-publicos-'));
  repo = join(base, 'produto-aberto');
  listas = join(base, '.listas');
  mkdirSync(repo);
  mkdirSync(listas);
  mkdirSync(join(base, 'projeto-vizinho'));
  mkdirSync(join(base, 'vizinho-liberado'));
  writeFileSync(join(listas, 'termos.txt'), '# clientes\nEmpresa Fictícia\n');
  writeFileSync(join(listas, 'autorizados.txt'), 'vizinho-liberado\n');
  execFileSync('git', ['init', '-q', '-b', 'main', repo]);
  git('config', 'user.email', NOREPLY);
  git('config', 'user.name', 'Dono');
  writeFileSync(join(repo, 'a.md'), 'inicio\n');
  git('add', 'a.md');
  git('commit', '-q', '-m', 'inicio');
});

after(() => rmSync(base, { recursive: true, force: true }));

// HOOK-NOMES-001
test('commit_semNomeProibido_passa', () => {
  prepararStage('b.md', 'texto comum sobre o produto-aberto\n');
  assert.equal(rodar('git commit -m "docs: texto comum"'), null);
});

// HOOK-NOMES-002
test('commit_comPastaVizinhaNoArquivo_recusaEDizOnde', () => {
  prepararStage('c.md', 'veja o Projeto-Vizinho aqui\n');
  const motivo = rodar('git commit -m "docs: texto"');
  assert.match(motivo, /c\.md: "projeto-vizinho"/);
  git('reset', '-q');
});

// HOOK-NOMES-003
test('commit_comTermoDaListaNaMensagem_recusa', () => {
  assert.match(rodar('git commit -m "docs: pedido da Empresa Fictícia"'), /mensagem do commit: "empresa fictícia"/);
});

// HOOK-NOMES-004
test('nomeAutorizado_passa', () => {
  assert.equal(rodar('git commit -m "docs: cita o vizinho-liberado"'), null);
});

// HOOK-NOMES-005
test('palavraQueSoContemONome_passa', () => {
  assert.equal(rodar('git commit -m "docs: meu-projeto-vizinho-novo"'), null);
});

// HOOK-NOMES-006
test('coautorNaMensagemEmArquivo_recusa', () => {
  const arquivo = join(base, 'msg.txt');
  writeFileSync(arquivo, 'docs: texto\n\nCo-authored-by: Alguem <a@b.c>\n');
  assert.match(rodar(`git commit -F "${arquivo}"`), /linha Co-authored-by/);
});

// HOOK-NOMES-007
test('emailForaDoNoreply_recusaOCommit', () => {
  git('config', 'user.email', 'pessoa@firma.example');
  const motivo = rodar('git commit -m "docs: texto"');
  git('config', 'user.email', NOREPLY);
  assert.match(motivo, /e-mail do git fora do noreply/);
});

// HOOK-NOMES-008
test('corpoDoPrEmArquivoAbsoluto_eLido', () => {
  git('update-ref', 'refs/remotes/origin/main', 'HEAD');
  const corpo = join(base, 'corpo.md');
  writeFileSync(corpo, 'Pedido do projeto-vizinho.\n');
  assert.match(rodar(`gh pr create --title "x" --body-file "${corpo}"`), /PR: "projeto-vizinho"/);
});

// HOOK-NOMES-009
test('prPeloMcp_conferePeloTituloECorpo', () => {
  const motivo = rodar('', { tool_name: 'mcp__github__create_pull_request', tool_input: { title: 'x', body: 'cita o projeto-vizinho' } });
  assert.match(motivo, /PR: "projeto-vizinho"/);
});

// HOOK-NOMES-010
test('commitEmOutroRepositorio_naoEConferido', () => {
  const outro = join(base, 'projeto-vizinho');
  execFileSync('git', ['init', '-q', outro]);
  assert.equal(rodar(`git -C "${outro}" commit -m "cita Empresa Fictícia"`), null);
});

// HOOK-NOMES-011
test('outroComando_passaSemConferir', () => {
  assert.equal(rodar('git status'), null);
});
