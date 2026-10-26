// Confere que nada privado vai para o repositório público (ADR-004): procura, em todo arquivo de
// texto rastreado pelo git, os termos de uma lista que fica FORA do repositório (citar o nome de
// um cliente num arquivo público já o expõe). Imagem não se lê: os prints só podem vir de
// repositórios públicos, e quem os tira confere.
//
// Uso: node scripts/verificar-publico.mjs [arquivo-de-termos]
// Termos, um por linha (# comenta), sem diferença de caixa. Padrão: ~/.rastro-publico/termos.txt
// ou a variável RASTRO_TERMOS. Sai com 1 se achar algo, e com 2 se a lista não existir.
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';

const listPath = process.argv[2] ?? process.env.RASTRO_TERMOS ?? join(homedir(), '.rastro-publico', 'termos.txt');
if (!existsSync(listPath)) {
  console.error(`Lista de termos não encontrada: ${listPath}`);
  process.exit(2);
}

const terms = readFileSync(listPath, 'utf8')
  .split(/\r?\n/)
  .map((line) => line.trim())
  .filter((line) => line && !line.startsWith('#'))
  .map((term) => term.toLowerCase());

const files = execFileSync('git', ['ls-files', '-z'], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }).split('\0').filter(Boolean);
const hits = [];
for (const file of files) {
  if (!existsSync(file)) continue;
  const buffer = readFileSync(file);
  // Binário (imagem, fonte): tem byte zero nos primeiros 8 KB.
  if (buffer.subarray(0, 8192).includes(0)) continue;
  const lines = buffer.toString('utf8').split(/\r?\n/);
  lines.forEach((line, index) => {
    // Barra invertida (também a escapada no código, como em 'C:\\pasta') conta como barra.
    const lower = slashes(line.toLowerCase());
    for (const term of terms) {
      if (lower.includes(slashes(term))) hits.push(`${file}:${index + 1}: ${term}`);
    }
  });
}

function slashes(text) {
  return text.replace(/[\\/]+/g, '/');
}

if (hits.length === 0) {
  console.log(`Nada privado em ${files.length} arquivos (${terms.length} termos).`);
  process.exit(0);
}
console.log(hits.join('\n'));
console.log(`\n${hits.length} ocorrência(s) em ${new Set(hits.map((h) => h.split(':')[0])).size} arquivo(s).`);
process.exit(1);
