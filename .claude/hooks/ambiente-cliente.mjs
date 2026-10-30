// Governança do ambiente do cliente (seção "Mudança no ambiente do cliente" do CLAUDE.md).
//
// O Rastro roda na máquina de quem instala o pacote. Mudança no que essa pessoa precisa ter,
// configurar ou saber (pacote, migration, CLI, MCP, porta, o que sai da máquina, CI e release)
// só fica pronta com a documentação no mesmo PR. A regra escrita dependia de alguém lembrar;
// este hook a confere na hora de abrir o PR.
//
// PreToolUse de Bash/PowerShell. Só olha `gh pr create`: compara o branch com a origin/main
// e, se ele mexe em arquivo de ambiente do cliente sem mexer em nenhum documento que trata
// disso, recusa o comando e diz o que conferir.
//
// Saída explícita: quando a mudança de fato não afeta o cliente, o corpo do PR leva a linha
// "Ambiente do cliente: sem mudança (<motivo>)". O motivo fica registrado no PR.
//
// Nunca trava o trabalho por defeito próprio: erro interno (git ausente, branch sem origin)
// libera o comando e avisa no stderr.

import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const RAIZ = process.env.CLAUDE_PROJECT_DIR || join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const MARCA_SEM_MUDANCA = /Ambiente do cliente:\s*sem mudança/i;

// Arquivos que mudam o ambiente do cliente, cada um com o que costuma precisar de atualização.
const AMBIENTE = [
  { padrao: /^backend\/src\/Rastro\.Api\/Rastro\.Api\.csproj$/, motivo: 'o que vai dentro do pacote', docs: 'README.md e docs/pacote/README.md' },
  { padrao: /^backend\/src\/Rastro\.Api\/(Program\.cs|appsettings[^/]*\.json|OriginGuardMiddleware\.cs)$/, motivo: 'porta, AllowedHosts ou configuração da API', docs: 'README.md e SECURITY.md' },
  { padrao: /^backend\/src\/Rastro\.Infrastructure\/Migrations\//, motivo: 'banco do cliente (migration)', docs: 'README.md (atualização do pacote) e o plano' },
  { padrao: /^backend\/src\/Rastro\.Infrastructure\/(Configuration|Storage)\//, motivo: 'configuração, pasta de dados ou variável de ambiente', docs: 'README.md e docs/pacote/README.md' },
  { padrao: /^backend\/src\/Rastro\.Infrastructure\/Repositories\/RepositoryCatalog\.cs$/, motivo: 'tipo de repositório ou caminho aceito', docs: 'README.md e a ADR da decisão' },
  { padrao: /^backend\/src\/Rastro\.Cli\//, motivo: 'comando da CLI', docs: 'README.md e docs/pacote/README.md' },
  { padrao: /^backend\/src\/Rastro\.Mcp\//, motivo: 'ferramenta do MCP', docs: 'README.md e McpInstructions' },
  { padrao: /^backend\/src\/Rastro\.Ai\/Providers\//, motivo: 'o que sai da máquina (provedor de IA)', docs: 'README.md e SECURITY.md' },
  { padrao: /^extractor-ts\/(package\.json|extract\.mjs)$/, motivo: 'extrator Angular (Node na máquina)', docs: 'README.md, docs/pacote/README.md e a ADR-005' },
  { padrao: /^(scripts\/empacotar\.ps1|VERSION|Dockerfile|docker-compose[^/]*)$/, motivo: 'empacotamento, versão ou Docker', docs: 'README.md' },
  { padrao: /^\.github\/workflows\//, motivo: 'CI ou release', docs: 'README.md (como publicar) e CLAUDE.md' },
];

// Documentos e governança que tratam do ambiente do cliente.
const DOCUMENTOS = [
  /^README\.md$/,
  /^docs\/pacote\/README\.md$/,
  /^SECURITY\.md$/,
  /^docs\/adr\//,
  /^docs\/arquitetura\/visao\.md$/,
  /^CLAUDE\.md$/,
  /^\.claude\/skills\//,
  /^backend\/src\/Rastro\.Mcp\/McpInstructions\.cs$/,
];

function lerEntrada() {
  try {
    return JSON.parse(readFileSync(0, 'utf8') || '{}');
  } catch {
    return null;
  }
}

function git(args) {
  return execFileSync('git', ['-C', RAIZ, ...args], { encoding: 'utf8', timeout: 5000, stdio: ['ignore', 'pipe', 'ignore'] });
}

// O que o branch muda em relação à main (commits do branch e o que ainda não foi commitado).
function arquivosDoBranch() {
  const base = git(['merge-base', 'HEAD', 'origin/main']).trim();
  const commitados = git(['diff', '--name-only', base, 'HEAD']);
  const locais = git(['diff', '--name-only', 'HEAD']);
  return [...new Set(`${commitados}\n${locais}`.split(/\r?\n/).map((l) => l.trim()).filter(Boolean))];
}

function conferir(entrada) {
  const comando = String(entrada.tool_input?.command || '');
  if (!/\bgh\b[^\n]*\bpr\b[^\n]*\bcreate\b/.test(comando)) return;
  if (MARCA_SEM_MUDANCA.test(comando)) return;
  // Corpo do PR em arquivo (--body-file <arquivo>): a marca também vale lá.
  const corpo = comando.match(/--body-file(?:\s+|=)(["']?)([^\s"']+)\1/);
  if (corpo && corpo[2] !== '-') {
    try {
      if (MARCA_SEM_MUDANCA.test(readFileSync(join(RAIZ, corpo[2]), 'utf8'))) return;
    } catch {
      // Arquivo do corpo fora do repositório ou ilegível: segue a conferência pelos arquivos.
    }
  }

  const arquivos = arquivosDoBranch();
  const ambiente = arquivos.flatMap((arquivo) => {
    const regra = AMBIENTE.find((r) => r.padrao.test(arquivo));
    return regra ? [{ arquivo, ...regra }] : [];
  });
  if (ambiente.length === 0) return;
  if (arquivos.some((arquivo) => DOCUMENTOS.some((d) => d.test(arquivo)))) return;

  process.stdout.write(JSON.stringify({
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'deny',
      permissionDecisionReason:
        'Ambiente do cliente: este branch muda o que quem instala o Rastro precisa ter, configurar ou saber, ' +
        'e não atualiza nenhum documento que trata disso.\n' +
        ambiente.map((a) => `  - ${a.arquivo}: ${a.motivo} (conferir ${a.docs})`).join('\n') +
        '\nAtualize a documentação e a governança no mesmo PR (lista na seção "Mudança no ambiente do cliente" do CLAUDE.md). ' +
        'Se a mudança de fato não afeta o cliente, ponha no corpo do PR a linha "Ambiente do cliente: sem mudança (<motivo>)".',
    },
  }));
}

try {
  const entrada = lerEntrada();
  if (entrada) conferir(entrada);
} catch (erro) {
  process.stderr.write(`ambiente-cliente: conferência não rodou (${erro?.message ?? erro}); o comando segue.\n`);
}
process.exit(0);
