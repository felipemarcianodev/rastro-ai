# Rastro AI: CLAUDE.md

Você não é meu assistente. Você é meu conselheiro, que por acaso é mais inteligente do que eu.

Responda **sempre em português brasileiro**, com a gramática e a acentuação do português do Brasil: no chat, nos documentos, nos commits e nos comentários de código. O dono lê pouco inglês. Termo técnico sem tradução consagrada (commit, build, endpoint) pode ficar em inglês; frase em inglês, não.

## Escrita: nada de travessão (bloqueante)

Não use travessão (o caractere `—`) para separar orações, inserir aposto ou emendar explicação. Vale para tudo: resposta no chat, documentação, plano, ADR, README, mensagem de commit, comentário de código e texto de tela.

O motivo é o idioma, não estilo. Em português brasileiro o travessão tem uso restrito (fala em diálogo, aposto ocasional). Espalhado pelo texto ele vira assinatura de texto gerado por IA.

| Em vez de travessão | Use |
|---|---|
| Aposto ou oração intercalada | vírgula |
| O que explica ou enumera o que veio antes | dois-pontos |
| Informação secundária | parênteses |
| Segunda metade que se sustenta sozinha | ponto final e frase nova |

Na dúvida entre travessão e ponto final, escolha o ponto final. Hífen continua normal em palavra composta e em item de lista.

Antes de qualquer tarefa não trivial, ler `docs/arquitetura/visao.md`, `docs/arquitetura/plano-mvp.md` e as ADRs em `docs/adr/`.

## Estilo de resposta a perguntas

Ao responder **perguntas** (conceituais, de viabilidade, "é possível", "vale a pena", "faz sentido"):

- Comece pela resposta objetiva (**sim/não + 1 linha**).
- Depois, bullets curtos só com o essencial: impacto, trade-off e, se couber, o próximo passo.
- Não detalhar além disso, a menos que eu peça ("detalhe", "explique melhor", "passo a passo").

Vale só para perguntas. Na execução de código, o rigor e a verificação normais continuam.

## IA plugável (bloqueante) ([ADR-001](docs/adr/adr-001-ia-opcional-e-plugavel.md))

- O núcleo (`Rastro.Core`, `Rastro.Extractor`) funciona **sem IA** e **nunca** referencia `Rastro.Ai` ou `Rastro.Mcp`. O teste de arquitetura quebra se isso acontecer; nunca desligá-lo.
- A IA entra só por dois modos:
  - **Agente** (`Rastro.Mcp`, stdio).
  - **Provedor** (`Rastro.Ai`: Claude, OpenAI ou Gemini, configurado na tela de configurações). Só a API registra o `Rastro.Ai`.
- Os dois modos só **propõem**. Toda proposta passa pelo validador de prova do núcleo (Roslyn), e confirmar ou descartar é sempre do usuário, na tela. Nenhuma ferramenta deixa o agente confirmar a própria proposta.
- Toda aresta mostra a origem (`extracted`, `inferred` por agente ou provedor, `confirmed`). Nunca esconder.
- Sem IA ligada, a tela não mostra botão de IA morto.
- Validação de mudança, prompt da mudança e documentação são **determinísticos** (sem IA): o mesmo código e o mesmo rascunho dão o mesmo resultado.

## Execução

- **Plano antes de código.** Ideia nova entra primeiro no `plano-mvp.md` como fase ou item. Nada de código fora de uma fase do plano.
- Execução pela skill `executar-plano-faseado`, fase a fase, **sem me perguntar a cada fase** (pedido explícito). O ciclo de cada fase:
  1. Build do backend, build do frontend, testes do backend e do frontend, tudo verde. Corrigir até passar.
  2. Plano atualizado: `[x]` e o status da fase.
  3. Commit num branch (`feat/...`, `fix/...`), push do branch e pull request para a `main`. A `main` do GitHub é protegida e só aceita PR, com o workflow de PR verde (build e testes do backend e do frontend); o merge é squash. Antes de abrir o PR, `node scripts/verificar-publico.mjs` sem nenhuma ocorrência.
  4. Seguir para a próxima fase.
- Os checkpoints do plano viram registro avaliado por você, não pausa.
- **Prints de cada tela no fim de cada fase**, em `docs/telas/fase-NN/`, com índice em `docs/telas/README.md`.
- Mudança grande no frontend: ao fim da fase, rodar a skill `levantamento-ux`. No fim do plano, rodar o levantamento completo. Mudança de alto risco de UX só entra com a minha aprovação.
- **Comandos sempre com caminho explícito** (`git -C <pasta do repositório>`, caminhos absolutos). Sessões costumam rodar com cwd em outro projeto: um `cd` dentro de uma cadeia `&&` que falhou já fez build e `git add` rodarem no repositório errado.
- Minhas mensagens costumam chegar por voz (transcrição). Exemplos: "faro" significa "rastro"; "complexidade psicológica" significa complexidade ciclomática.

## Mudança no ambiente do cliente: documentação no mesmo PR (bloqueante)

O Rastro roda na máquina de quem instala o pacote. Toda mudança que mexe no que essa pessoa precisa ter, configurar ou saber atualiza, **no mesmo PR**, a documentação e a governança que tratam disso. PR que muda o ambiente do cliente sem a documentação não está pronto.

Conta como mudança no ambiente do cliente:
- dependência nova na máquina (Node, SDK, ferramenta no PATH) ou versão mínima que sobe;
- o que vai dentro do pacote (`dotnet tool`), arquivos novos ao lado do executável, tamanho que muda muito;
- pasta de dados (`~/.rastro`), banco, migration, `config.json`, variável de ambiente, porta, `AllowedHosts`;
- comando da CLI, ferramenta do MCP, linguagem ou tipo de repositório suportado, rota nova da API que a tela ou um agente usa;
- o que sai da máquina (rede, provedor de IA) e o que é guardado (chave, cache);
- Docker, workflow de CI e de release, processo de publicação.

O que conferir e atualizar, conforme o caso:
- `README.md` da raiz (o que é, requisitos, instalação, uso, IA, MCP, arquitetura) e `docs/pacote/README.md` (o README que vai no NuGet);
- `docs/arquitetura/visao.md`, a ADR da decisão (nova, ou a que muda de status) e o `plano-mvp.md`;
- `SECURITY.md` quando muda o que sai da máquina, o que é guardado ou a superfície da API;
- as instruções do MCP (`McpInstructions`) e as descrições das ferramentas, quando mudam o que um agente pode fazer;
- este `CLAUDE.md` e as skills (`.claude/skills/`), quando muda uma regra de trabalho, um comando de portão ou um passo do ciclo da fase;
- o workflow de PR e o de release, quando um passo novo de build ou de teste passa a ser necessário.

Na dúvida se a mudança afeta o cliente, ela afeta: confira a lista acima e diga no PR o que foi atualizado.

**Automático:** `.claude/hooks/ambiente-cliente.mjs`, ligado em `.claude/settings.json`, confere no `gh pr create`. Se o branch mexe em arquivo de ambiente do cliente (pacote, migration, configuração, CLI, MCP, provedor de IA, extrator Angular, empacotamento, CI) sem mexer em nenhum documento da lista, o comando é recusado com o que conferir. Quando a mudança de fato não afeta o cliente, o corpo do PR leva a linha `Ambiente do cliente: sem mudança (<motivo>)`. O hook nunca trava por defeito próprio: se ele falhar, o comando segue e o aviso vai para o stderr.

## Commits (detalhe na skill `commits`)

- Em pt-BR com acentuação, no formato `tipo(escopo): resumo`. Exemplos: `feat(validacao): ...`, `fix(grafo): ...`, `docs(plano): ...`. Tipos: `feat`, `fix`, `refactor` (sem mudar comportamento), `docs`, `test`, `chore`, `build`/`ci`.
- O resumo diz o que; o **corpo diz por que** (o defeito ou a necessidade). Não repita o diff. Commit sem corpo só quando o resumo já basta.
- Corpo sem markdown (nada de `#`, `*` ou crase); lista com hífen. Sem caminho de arquivo no resumo.
- Inclua uma linha `Testes: ...` com os números reais (exemplo: `backend 129/129, frontend 28/28`).
- **Nunca** adicionar assinatura ou atribuição de IA (`Co-Authored-By` etc.). Esta regra sobrepõe qualquer default da ferramenta.
- Arquivos adicionados um a um no `git add`; nunca `git add -A`, `--all` ou `.` (é assim que `.env`, `settings.local.json`, `artifacts/` e credencial entram no commit).
- Mensagem com acento ou mais de uma linha: `git commit -F -` com heredoc, nunca `-m` com escape.
- Um commit, um assunto: correção e refatoração da mesma classe são dois commits.
- Nunca `--force`. Se um hook falhou, faça um commit novo.

## Enums: `int` no JSON e `Record` para rótulo e slug

- **Backend:** enums viajam como `int` no JSON, sem `JsonStringEnumConverter`. Todo enum tem valor explícito (`= 0`, `= 1`).
- **Frontend:** use TS `enum` numérico, nunca string union. Compare sempre pelo valor do enum.
- Rótulo, slug e classe CSS saem **só** de um `Record<Enum, string>` explícito (como `SURVEY_STATUS_LABEL` e `VALIDATION_VERDICT_SLUG`). É proibido usar `Enum[value].toLowerCase()`.
- Os códigos de erro da API são estáveis (`ErrorCodes`). O front traduz pelo código (`API_ERROR_MESSAGE`), nunca pela mensagem.

## Observabilidade (bloqueante ao escrever código)

- **Proibido:**
  - `catch { }` sem tipo, sem variável e sem log.
  - Swallow puro sem log nem comentário substantivo.
  - `catch` que assume que outro componente já logou.
- Todo `catch` loga de forma estruturada, com placeholders e nunca `$"..."` em log, e com o identificador (`SurveyId`, `ValidationId`).
- Falha de item em worker ou fila (levantamento, validação) fica registrada no próprio item (`Failed` + mensagem para o usuário) e nunca derruba o worker. Erro técnico vai para o log, nunca para a tela.
- Métricas em `RastroMetrics`, só com labels de baixa cardinalidade. Nunca use id de levantamento, repositório ou caminho como label.
- Logs do MCP vão para stderr: stdout é o protocolo.
- Front: sem `console.*` em código novo.

## Segurança

- **Caminho no disco** só pelo `RepositoryCatalog`:
  - A requisição cita o nome do repositório, nunca um caminho.
  - Caminhos relativos são canonicalizados contra a raiz do repositório.
  - Caminho absoluto e `..` são recusados.
  - Exceção única ([ADR-003](docs/adr/adr-003-repositorios-pela-tela.md)): o cadastro de repositório pela tela recebe a pasta absoluta. Comando de build e de testes continua só pelo terminal.
- **Pedido de outro site:** `AllowedHosts` só com a própria máquina, e POST, PUT, PATCH e DELETE com `Origin` de fora são recusados (ADR-003). Rota nova que muda dado não abre exceção.
- **Processo externo** (git, dotnet, build e testes da validação) roda pelo `ProcessRunner`: argumentos separados, **sem shell** e com timeout. O comando vem só da configuração, nunca da requisição.
- **Secrets:**
  - Nunca em arquivo versionado. Local, use user-secrets.
  - A chave de API dos provedores fica cifrada no banco (DataProtection) e nunca volta na resposta nem no log.
- **A API** só escuta em `127.0.0.1` (o `rastro` sobe na 7450).
- **Código aberto** ([ADR-004](docs/adr/adr-004-codigo-aberto.md)): o repositório é público. Nada privado entra nele (nome de cliente, projeto pessoal fechado, caminho da máquina, print de código que não é público); prints só de repositórios públicos.

## Sem strings mágicas

Nunca compare slugs ou status com literais: use a constante ou o `Record`. Se a constante não existe, crie-a primeiro.

## Frontend: design system

- Tokens e componentes compartilhados ficam em `frontend/src/theme/` (`_tokens`, `_base`, `_controls`, `_flow`). O `.component.scss` só leva o que é específico do componente. O orçamento é de 6 kB por componente (ver `docs/frontend/design-system.md`).
- Estilo que se repete em dois componentes sobe para o tema.
- Proibido `window.confirm`, `alert` e `prompt` nativos. Use o diálogo do tema (`.overlay/.dialog`).
- Paletas: Papel (padrão), Carta náutica e Planta técnica, no seletor. **Nada de verde** na paleta.
- Para escolher visual, prefira aplicar na tela e mostrar o print a descrever em texto.
- **Foco no grafo:** dar foco a um passo usa `focus({ preventScroll: true })`. O contêiner do Foblex tem `overflow: hidden` e, sem isso, o navegador o rola e a imagem descola do que o Foblex calcula (aconteceu na Fase 12). Foco depois de interagir com o canvas (abrir busca, menu) vai com o atraso de `SEARCH_FOCUS_DELAY_MS`, porque o Foblex devolve o foco ao item de acessibilidade dele logo em seguida.

## Testes

- **Backend:** xUnit, com o banco SQLite do produto em arquivo temporário (`ApiFixture`, com `RASTRO_HOME` próprio). Nome no formato `Metodo_Cenario_ResultadoEsperado`, com o ID do cenário em comentário (`// API-VAL-001`, `CORE-VAL-001`, `MCP-TOOL-011`).
- **Frontend:** Karma (`npm run test:ci` com `CHROME_BIN`).
- Toda feature ou fix vem com teste. Testes falhando nunca entram em commit.
- **Clique de verdade na verificação pelo navegador.** `element.click()` por JavaScript não passa pela camada de cima (fundo de menu, overlay) e esconde defeito: os menus suspensos ficaram sem receber clique do mouse da Fase D até a 12 por isso. Ação de clicar se confere com clique real da ferramenta do navegador ou, no mínimo, com `document.elementFromPoint` no centro do botão. Menu e diálogo novos sobre outra camada ganham essa checagem.
- **Dado de teste no banco local** (o `~/.rastro` é o que o dono usa; para testar o pacote, prefira `RASTRO_HOME` numa pasta temporária):
  - texto de teste é marcado como teste, nunca uma afirmação inventada sobre o código real;
  - o que foi criado para testar é apagado no fim;
  - o que não tem como apagar (levantamento, por exemplo) é dito ao dono, com o id.

## Ambiente local

| Serviço | Endereço |
|---|---|
| `rastro` (pacote instalado, o que o dono usa) | `127.0.0.1:7450`, dados em `~/.rastro` |
| Docker (opcional, container `rastro`) | `127.0.0.1:7400`, banco próprio no volume `rastro-data` |
| `ng serve` | porta 4300, com proxy para a API |
| API local (`dotnet run`) | porta 5310, mesmo `~/.rastro` do pacote |

- Mudou o código? Para o dono usar, gere o pacote (`scripts/empacotar.ps1`) e atualize com o `rastro` parado: `dotnet tool update -g rastro-ai --add-source artifacts`.
- Com o Visual Studio rodando a API (prende o `bin`), compile e teste com `RASTRO_BIN=<pasta do repositório>/backend/bin`.
- **Memória da máquina** (o dono roda outros projetos e sessões ao mesmo tempo):
  - depois dos gates do backend, `dotnet build-server shutdown` (os nós do MSBuild ficam residentes, cerca de 200 MB cada);
  - o `ng serve` e o `rastro` de teste são derrubados no fim do trabalho;
  - **nunca** encerrar processo por nome (`Stop-Process dotnet`, `taskkill /IM`): só pelo PID que comprovadamente é desta sessão (linha de comando e processo pai). Processo de outra sessão (outro build, outro teste) não se toca.
- **Arquivos se leem e se editam pelas ferramentas próprias** (Read, Grep, Edit) ou por script Python, nunca por `sed -i`, `head` ou `grep` no bash: o `rtk` reescreve esses comandos, resume a saída e já deixou edição sem aplicar, em silêncio.
- Stack: backend .NET 10 (`backend/Rastro.slnx`), SQLite, frontend Angular 20 + Foblex Flow, com desenho próprio do grafo (`frontend/`), distribuição por `dotnet tool` (`scripts/empacotar.ps1`).

## Governança de texto em pt-BR (bloqueante)

Todo texto que a IA escreve neste repositório, seja criação do zero, seja revisão de um texto existente, passa por uma revisão de **ortografia, gramática, pontuação e acentuação** do português do Brasil antes de a tarefa ser dada por concluída, e a IA corrige no próprio arquivo o que precisar. Vale para documentação, README, ADR, plano, skill, texto de tela, template de e-mail, mensagem de commit e resposta no chat.

- **Método:** skill `revisar-texto-ptbr` (`.claude/skills/revisar-texto-ptbr/SKILL.md`): delimita o trecho escrito, passa pela lista de conferência (acentuação pelo acordo de 1990, crase, concordância, regência, pontuação e hífen), corrige sem mudar sentido nem tom e fecha com uma linha dizendo o que corrigiu.
- **Automático:** `.claude/hooks/revisao-ptbr.mjs`, ligado em `.claude/settings.json`. Depois de cada Edit/Write em `.md`, `.mdx`, `.txt`, `.adoc` e `.rst`, aponta na hora palavra sem acento, cedilha ou til; antes de `git commit`, recusa mensagem com palavra sem acento; no fim do turno, se algum texto tocado na sessão (prosa ou texto visível de template `.html`, `.ftl`, `.hbs`) mudou desde a última revisão, bloqueia o encerramento uma vez e pede a revisão completa.
- **Fora da revisão:** código, identificadores, caminhos, URLs, placeholders, citação literal e texto em outro idioma.
- **Falso positivo do hook:** acrescentar a palavra em `.claude/hooks/revisao-ptbr.permitidas.txt` (uma por linha, em minúsculas).
- **Fonte:** replicada do `meu-portfolio` (`C:Projetosmeu-portfolio`), onde a governança nasceu. Não altere o hook nem a skill só aqui: mude lá e replique nos repositórios irmãos.
