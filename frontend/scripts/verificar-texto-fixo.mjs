// Varredura de texto fixo nos templates da tela (ADR-009): todo texto visível sai das chaves de
// tradução. Lê os .html e os templates inline dos componentes e falha se achar texto entre tags ou
// atributo visível (title, aria-label, placeholder, alt) escrito à mão, com letras.
// Exceções de propósito ficam em texto-fixo-permitido.txt (uma por linha).
// Uso: node frontend/scripts/verificar-texto-fixo.mjs
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const app = join(here, '..', 'src', 'app');
const allowed = new Set(
  readFileSync(join(here, 'texto-fixo-permitido.txt'), 'utf8')
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l && !l.startsWith('#')),
);

function* files(dir) {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) yield* files(path);
    else if (/\.html$/.test(name) || (/\.ts$/.test(name) && !/\.spec\.ts$/.test(name))) yield path;
  }
}

function templates(path, source) {
  if (path.endsWith('.html')) return [source];
  return [...source.matchAll(/template:\s*`([\s\S]*?)`/g)].map((m) => m[1]);
}

// Texto que alguém lê: tira comentários, interpolação e blocos de controle; o resto entre tags conta.
function findings(template) {
  const clean = template
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/\{\{[\s\S]*?\}\}/g, ' ')
    .replace(/@let\s[^;]*;/g, ' ')
    .replace(/&[a-z]+;|&#\d+;/gi, ' ')
    .replace(/@(if|else if|else|for|switch|case|default|empty|defer|placeholder|loading)\b[^{]*\{/g, ' ')
    .replace(/<(svg|code|pre|kbd)\b[\s\S]*?<\/\1>/g, ' ');
  const found = [];
  for (const m of clean.matchAll(/>([^<>]+)</g)) {
    const text = m[1].replace(/[{}]/g, ' ').replace(/\s+/g, ' ').trim();
    if (/\p{L}{2,}/u.test(text)) found.push(text);
  }
  for (const m of clean.matchAll(/\s(title|aria-label|placeholder|alt|aria-description)="([^"]*)"/g)) {
    if (/\p{L}{2,}/u.test(m[2])) found.push(`${m[1]}="${m[2]}"`);
  }
  return found.filter((t) => !allowed.has(t));
}

let total = 0;
for (const path of files(app)) {
  for (const template of templates(path, readFileSync(path, 'utf8'))) {
    for (const text of findings(template)) {
      total++;
      console.error(`${relative(join(here, '..'), path)}: ${text}`);
    }
  }
}
if (total > 0) {
  console.error(`\n${total} texto(s) fixo(s) no template. Use o pipe translate (skill traduzir-texto) ou, se for de propósito, acrescente em frontend/scripts/texto-fixo-permitido.txt.`);
  process.exit(1);
}
console.log('Nenhum texto fixo nos templates.');
