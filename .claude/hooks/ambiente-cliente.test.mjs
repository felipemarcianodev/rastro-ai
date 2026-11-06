// Testes do hook ambiente-cliente.mjs: um repositório git temporário com a origin/main, um arquivo
// mudado no branch e o comando `gh pr create` na entrada do hook.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HOOK = join(dirname(fileURLToPath(import.meta.url)), 'ambiente-cliente.mjs');
const PR = { tool_input: { command: 'gh pr create --title "x" --body "corpo"' } };

function repositorio(arquivos) {
  const raiz = mkdtempSync(join(tmpdir(), 'ambiente-cliente-'));
  const git = (...args) => execFileSync('git', ['-C', raiz, ...args], { stdio: 'ignore' });
  const escrever = (caminho, texto) => {
    mkdirSync(dirname(join(raiz, caminho)), { recursive: true });
    writeFileSync(join(raiz, caminho), texto);
  };
  git('init', '-q');
  for (const caminho of arquivos) escrever(caminho, 'antes\n');
  git('add', '-A');
  git('-c', 'user.email=t@t.test', '-c', 'user.name=t', 'commit', '-qm', 'base');
  git('update-ref', 'refs/remotes/origin/main', 'HEAD');
  return { raiz, mudar: (caminho) => escrever(caminho, 'depois\n'), apagar: () => rmSync(raiz, { recursive: true, force: true }) };
}

function rodar(raiz, entrada = PR) {
  const saida = spawnSync(process.execPath, [HOOK], { input: JSON.stringify(entrada), encoding: 'utf8', env: { ...process.env, CLAUDE_PROJECT_DIR: raiz } });
  return saida.stdout ? JSON.parse(saida.stdout).hookSpecificOutput : null;
}

const LIVE = 'backend/src/Rastro.Infrastructure/Database/Live/SqlServerLiveDatabase.cs';

test('código do banco vivo sem documento: o PR é recusado com o motivo', () => {
  const repo = repositorio([LIVE, 'README.md']);
  try {
    repo.mudar(LIVE);
    const saida = rodar(repo.raiz);
    assert.equal(saida?.permissionDecision, 'deny');
    assert.match(saida.permissionDecisionReason, /banco vivo do cliente/);
    assert.match(saida.permissionDecisionReason, /SECURITY\.md e a ADR-006/);
  } finally {
    repo.apagar();
  }
});

test('código do banco vivo com o SECURITY.md no mesmo branch: o PR segue', () => {
  const repo = repositorio([LIVE, 'SECURITY.md']);
  try {
    repo.mudar(LIVE);
    repo.mudar('SECURITY.md');
    assert.equal(rodar(repo.raiz), null);
  } finally {
    repo.apagar();
  }
});

test('dependência nova no csproj da infraestrutura sem documento: recusado', () => {
  const csproj = 'backend/src/Rastro.Infrastructure/Rastro.Infrastructure.csproj';
  const repo = repositorio([csproj]);
  try {
    repo.mudar(csproj);
    assert.match(rodar(repo.raiz)?.permissionDecisionReason ?? '', /dependência que vai dentro do pacote/);
  } finally {
    repo.apagar();
  }
});

test('arquivo que não é do ambiente do cliente, ou a marca "sem mudança": o PR segue', () => {
  const repo = repositorio(['frontend/src/app/app.ts', LIVE]);
  try {
    repo.mudar('frontend/src/app/app.ts');
    assert.equal(rodar(repo.raiz), null);
    repo.mudar(LIVE);
    assert.equal(rodar(repo.raiz, { tool_input: { command: 'gh pr create --body "Ambiente do cliente: sem mudança (só teste)"' } }), null);
  } finally {
    repo.apagar();
  }
});
