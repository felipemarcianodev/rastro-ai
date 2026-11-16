// Textos do extrator que chegam à tela: as mensagens de erro (a API as repassa ao usuário) e o rótulo da ligação
// "ação do usuário no modal". O .NET passa o idioma em --lang (pt-BR, en-US ou es), o mesmo do Rastro no momento
// da análise (ADR-009). Sem --lang ou com um código desconhecido, vale o inglês, a reserva dos três idiomas.
// Diagnóstico e detalhe técnico (saída de erro) ficam em português: vão para o log, não para a tela.

export const IDIOMA_PADRAO = 'en-US';

const TEXTOS = {
  'pt-BR': {
    unexpectedArgument: (a) => `Argumento inesperado: ${a}.`,
    missingValue: (a) => `Falta o valor de ${a}.`,
    missingArgument: (a) => `Falta o argumento --${a}.`,
    invalidDepth: (v) => `A profundidade (--depth) precisa ser um número inteiro, 0 ou maior: "${v}".`,
    typescriptNotFound: () => 'O TypeScript não foi encontrado no repositório. Rode o npm install nele.',
    repositoryFolderNotFound: () => 'A pasta do repositório não foi encontrada.',
    unexpectedFailure: () => 'Não foi possível ler o código Angular. Veja o log da API para o detalhe.',
    projectFileNotFound: (p) => `O arquivo ${p} não foi encontrado no repositório.`,
    projectFileUnreadable: (p, detalhe) => `Não foi possível ler o ${p}: ${detalhe}`,
    projectWithoutFiles: (p) => `O ${p} não inclui nenhum arquivo TypeScript.`,
    invalidEntryPoint: (e) => `Ponto de entrada inválido: "${e}". Use Classe.metodo.`,
    methodNotFound: (e, m) => `Método "${e}" não encontrado no projeto: nenhum método chamado ${m}, com corpo, existe nele.`,
    methodNotFoundWithSuggestions: (e, m, lista, mais) =>
      `Método "${e}" não encontrado no projeto. Com o nome ${m} existe: ${lista}${mais === 0 ? '' : ` e mais ${mais}`}. Use Classe.metodo.`,
    ambiguousEntry: (e, classe, arquivos) =>
      `O ponto de entrada "${e}" é ambíguo: a classe ${classe} existe em mais de um arquivo (${arquivos}).`,
    modalAction: () => 'ação do usuário no modal',
    depthLimit: () => 'limite de profundidade',
    opensModal: (c) => `abre o modal ${c}`,
    anonymousClass: () => '(anônima)',
  },
  'en-US': {
    unexpectedArgument: (a) => `Unexpected argument: ${a}.`,
    missingValue: (a) => `Missing the value of ${a}.`,
    missingArgument: (a) => `Missing the --${a} argument.`,
    invalidDepth: (v) => `The depth (--depth) must be an integer, 0 or greater: "${v}".`,
    typescriptNotFound: () => 'TypeScript was not found in the repository. Run npm install in it.',
    repositoryFolderNotFound: () => 'The repository folder was not found.',
    unexpectedFailure: () => 'The Angular code could not be read. See the API log for details.',
    projectFileNotFound: (p) => `The file ${p} was not found in the repository.`,
    projectFileUnreadable: (p, detalhe) => `Could not read ${p}: ${detalhe}`,
    projectWithoutFiles: (p) => `${p} does not include any TypeScript file.`,
    invalidEntryPoint: (e) => `Invalid entry point: "${e}". Use Class.method.`,
    methodNotFound: (e, m) => `Method "${e}" was not found in the project: no method named ${m}, with a body, exists in it.`,
    methodNotFoundWithSuggestions: (e, m, lista, mais) =>
      `Method "${e}" was not found in the project. A method named ${m} exists in: ${lista}${mais === 0 ? '' : ` and ${mais} more`}. Use Class.method.`,
    ambiguousEntry: (e, classe, arquivos) =>
      `The entry point "${e}" is ambiguous: the class ${classe} exists in more than one file (${arquivos}).`,
    modalAction: () => 'user action in the modal',
    depthLimit: () => 'depth limit',
    opensModal: (c) => `opens the modal ${c}`,
    anonymousClass: () => '(anonymous)',
  },
  es: {
    unexpectedArgument: (a) => `Argumento inesperado: ${a}.`,
    missingValue: (a) => `Falta el valor de ${a}.`,
    missingArgument: (a) => `Falta el argumento --${a}.`,
    invalidDepth: (v) => `La profundidad (--depth) debe ser un número entero, 0 o mayor: "${v}".`,
    typescriptNotFound: () => 'No se encontró TypeScript en el repositorio. Ejecuta npm install en él.',
    repositoryFolderNotFound: () => 'No se encontró la carpeta del repositorio.',
    unexpectedFailure: () => 'No fue posible leer el código Angular. Consulta el log de la API para ver el detalle.',
    projectFileNotFound: (p) => `No se encontró el archivo ${p} en el repositorio.`,
    projectFileUnreadable: (p, detalle) => `No fue posible leer ${p}: ${detalle}`,
    projectWithoutFiles: (p) => `${p} no incluye ningún archivo TypeScript.`,
    invalidEntryPoint: (e) => `Punto de entrada inválido: "${e}". Usa Clase.metodo.`,
    methodNotFound: (e, m) => `No se encontró el método "${e}" en el proyecto: no existe en él ningún método llamado ${m}, con cuerpo.`,
    methodNotFoundWithSuggestions: (e, m, lista, mas) =>
      `No se encontró el método "${e}" en el proyecto. Con el nombre ${m} existe: ${lista}${mas === 0 ? '' : ` y ${mas} más`}. Usa Clase.metodo.`,
    ambiguousEntry: (e, clase, archivos) =>
      `El punto de entrada "${e}" es ambiguo: la clase ${clase} existe en más de un archivo (${archivos}).`,
    modalAction: () => 'acción del usuario en el modal',
    depthLimit: () => 'límite de profundidad',
    opensModal: (c) => `abre el modal ${c}`,
    anonymousClass: () => '(anónima)',
  },
};

// Aceita o código completo ou só o prefixo ("pt", "pt-PT" e "es-MX" caem no idioma certo).
export function idiomaDe(codigo) {
  const prefixo = String(codigo ?? '').trim().split(/[-_]/)[0].toLowerCase();
  if (prefixo === 'pt') return 'pt-BR';
  if (prefixo === 'es') return 'es';
  return IDIOMA_PADRAO;
}

// t('chave', ...argumentos) no idioma pedido.
export function tradutor(codigo) {
  const textos = TEXTOS[idiomaDe(codigo)];
  return (chave, ...args) => textos[chave](...args);
}
