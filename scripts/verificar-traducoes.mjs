// Documentação para quem usa nos três idiomas (ADR-009): cada documento existe em inglês (o arquivo
// principal), em português (.pt-BR.md) e em espanhol (.es.md), com os mesmos títulos, na mesma ordem
// e no mesmo nível. Roda no workflow de PR.
// Uso: node scripts/verificar-traducoes.mjs
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const raiz = join(dirname(fileURLToPath(import.meta.url)), '..');
const DOCUMENTOS = ['README.md', 'docs/pacote/README.md', 'SECURITY.md'];
const IDIOMAS = ['pt-BR', 'es'];

// Nível de cada título, fora dos blocos de código.
function niveis(texto) {
  const semCodigo = texto.replace(/^(```|~~~)[\s\S]*?^\1\s*$/gm, '');
  return [...semCodigo.matchAll(/^(#{1,6})\s+\S/gm)].map((m) => m[1].length);
}

let falhas = 0;
for (const documento of DOCUMENTOS) {
  const principal = join(raiz, documento);
  if (!existsSync(principal)) {
    console.error(`${documento}: não existe.`);
    falhas++;
    continue;
  }
  const referencia = niveis(readFileSync(principal, 'utf8'));
  for (const idioma of IDIOMAS) {
    const traducao = documento.replace(/\.md$/, `.${idioma}.md`);
    const caminho = join(raiz, traducao);
    if (!existsSync(caminho)) {
      console.error(`${traducao}: não existe (todo documento para quem usa tem as três versões).`);
      falhas++;
      continue;
    }
    const outro = niveis(readFileSync(caminho, 'utf8'));
    if (outro.join(',') !== referencia.join(',')) {
      const ate = referencia.findIndex((nivel, i) => outro[i] !== nivel);
      console.error(`${traducao}: os títulos não batem com ${documento} (${outro.length} contra ${referencia.length}; primeira diferença no título ${ate + 1}).`);
      falhas++;
    }
  }
}

if (falhas > 0) {
  console.error(`\n${falhas} problema(s). Atualize as três versões juntas (skill traduzir-texto).`);
  process.exit(1);
}
console.log(`Documentação nos três idiomas: ${DOCUMENTOS.length} documentos com os mesmos títulos.`);
