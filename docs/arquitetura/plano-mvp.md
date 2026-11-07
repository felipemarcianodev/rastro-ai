# Plano do Rastro AI (MVP): EXECUTADO (Fases 0 a 14, 17 a 21 e 23 a 29); Fases 15, 16 e 22 esperam o uso real; Fases 30 a 35 executadas (prints e UX da Fase 31 pendentes); plano do banco (Fases 36 a 42) EXECUTADO em 2026-11-06

> Criado em 2026-10-15. Visão e decisões: [`visao.md`](visao.md).
> Execução fase a fase, no mesmo molde da skill `executar-plano-faseado` de outro projeto do dono: **uma fase por vez, portões verdes, commit, e só então a próxima.**

## Portões de toda fase (não negociáveis)

1. **Build do backend:** `dotnet build backend/Rastro.slnx` com 0 erros.
2. **Suíte backend inteira:** `dotnet test backend/Rastro.slnx` com 0 falhas (Testcontainers a partir da Fase 2).
3. **Build do frontend:** `cd frontend && npm run build` sem erros (a partir da Fase 0).
4. **Teste falhou → corrigir até verde.** Teste não é "ajustado" para passar sem entender a causa.
5. **Este plano atualizado no mesmo commit:** fase marcada `[x]`, o que foi feito e os sub-itens descobertos como `[ ]`.
6. **Commit** `tipo(escopo): descrição` em pt-BR, com a linha `Testes: ...` e números reais, sem assinatura de IA. Depois do commit, **push na `main`** (`git push origin main`, nunca `--force`). Remoto: Azure DevOps `meu-portfolio/_git/rastro-ai`.
7. **Release:** só quando o dono pedir. A versão (arquivo `VERSION`) nasce na Fase 0. Tag e pipeline ficam para depois.
8. **Fase marcada "checkpoint":** parar e mostrar o resultado ao dono antes de seguir.
9. **UX:** fase com mudança grande no frontend termina com a skill `levantamento-ux` sobre as telas tocadas (baixo e médio risco entram na fase; alto risco vira `[ ]` e espera aprovação). **No fim do plano, `levantamento-ux` completo.**
10. **IA plugável ([ADR-001](../adr/adr-001-ia-opcional-e-plugavel.md)):** o teste de arquitetura (núcleo sem referência a IA) continua verde em toda fase.

Skills do projeto: `.claude/skills/executar-plano-faseado` e `.claude/skills/levantamento-ux`.

## Decisões do dono (2026-10-15)

| Tema | Decisão |
|---|---|
| Biblioteca de grafo no Angular | **Foblex Flow** (nativo Angular, drag and drop, arestas customizáveis) |
| Layout automático do grafo | **ELK.js** até a Fase 20; desde a Fase 21, desenho próprio (linha do tempo, em `core/graph/timeline-layout.ts`) |
| Acesso a dados | **EF Core + Npgsql**, grafo em coluna `jsonb` (trocado por **EF Core + SQLite** na Fase 14, ADR-002) |
| Leitura do código-alvo | **MSBuildWorkspace** (carrega a solution real, com tipos resolvidos, necessário para o DI) |
| IA | **Opcional e plugável, em dois modos** ([ADR-001](../adr/adr-001-ia-opcional-e-plugavel.md)). O núcleo (extração, grafo, edição, prompt, métricas, C4) funciona **sem IA nenhuma**. (a) **Modo agente:** um agente externo (ex.: o Claude Code, pela assinatura) usa o Rastro por um **servidor MCP** e faz o papel da IA, sem chave de API. (b) **Modo provedor:** o Rastro chama a IA direto, com **Claude, OpenAI ou Gemini** escolhidos numa tela de configuração, no mesmo padrão do `meu-portfolio` (`AiProvider`, `AiSettings`, `IAiTextService` por provedor, chaves cifradas com DataProtection, modelo por provedor, flag geral e orçamento diário de tokens). Os dois modos passam pelo **mesmo validador de prova**. |
| Repositório-alvo da validação | **repositório A** (job de alertas do Telegram). O Rastro só lê, nunca altera. |

---

## Fase 0: Fundação `[x]`

**Entrega:** esqueleto que compila e testa, sem funcionalidade.

**Status: concluída em 2026-10-15.**
- [x] `backend/Rastro.slnx` (.NET 10): `Rastro.Core` (`EdgeConfidence`, `ProposalOrigin`, `RastroSchema`, `RastroVersion`), `Rastro.Extractor` (vazio, sem IA), `Rastro.Cli` (`--version`), `Rastro.Api` (`GET /api/health`), `Rastro.Tests`. `Directory.Build.props` lê a versão do `VERSION` e trata warning como erro.
- [x] **Teste de arquitetura** (ADR-001): verifica `.csproj` e assembly de `Rastro.Core` e `Rastro.Extractor`. Comprovado que falha ao colocar um pacote de IA no núcleo.
- [x] `frontend/`: Angular 20 (`rastro-web`), SCSS, rotas, sem SSR, `ng serve` na **4300** com proxy de `/api` para a API local (5310), script `test:ci` (Chrome headless).
- [x] `docker/docker-compose.yml`: Postgres 16, API e web (nginx com proxy de `/api` pelo `container_name`). Portas livres na máquina, presas ao `127.0.0.1`: **web 7400, api 7410, postgres 5460**. Senha no `docker/.env` (fora do Git), modelo em `.env.example`.
- [x] `VERSION` (`0.1.0`), README com como rodar.
- [x] Os pacotes do Roslyn (`Microsoft.CodeAnalysis.*`, `Microsoft.Build.Locator` só no `Rastro.Cli`) entram na Fase 1, junto com o primeiro uso. O Locator no projeto de biblioteca quebra o build dos consumidores (MSBL001). **Feito:** entraram na Fase 1, com o `MSBuildLocator` só no `Rastro.Cli`.

**Portões:** backend build 0 erros, 8/8 testes; frontend build ok, 2/2 testes; Docker de pé com `/api/health` ok direto e pelo web.

## Fase 1: Extrator v0 (o coração) `[x]`

**Entrega:** `rastro analyze <solution> <Tipo.Metodo>` gera o JSON do fluxo.

**Status: concluída em 2026-10-15.**
- [x] `Rastro.Core/Graph`: `FlowGraph`, `FlowNode`, `FlowEdge`, `SourceLocation`, `NodeKind` (Entry, Method, Decision, Loop, Try, Return, Throw, External, Unresolved, Jump), `UnresolvedReason` (MultipleImplementations, NoImplementation, DynamicDispatch, Delegate, MessageDispatch, Reflection, DepthLimit) e `FlowGraphJson` (camelCase, enums int, nulos omitidos). Schema v1.
- [x] `Rastro.Extractor`: `SolutionLoader` (MSBuildWorkspace, ignora avisos de auditoria do NuGet), `EntryPointResolver`, `DiRegistrationIndex` (`Add*`/`TryAdd*`/`AddKeyed*` genérico e `typeof`), `FlowWalker` e `FlowExtractor`.
- [x] Percurso: chamadas na ordem de execução, inclusive dentro de lambdas; `if/else`, `switch` (case e pattern), `try/catch (when)/finally`, laços, `return` antecipado, `throw`, `break`/`continue` de laço; recursão sem duplicar nó; limite de profundidade.
- [x] Interface/abstrato: implementação única segue; fakes de projetos de teste não competem com a de produção; o DI desempata; sem desempate vira não resolvido com os candidatos.
- [x] Externo só quando é fronteira (chamada a partir de campo, propriedade, parâmetro ou local, fora de LINQ, coleções, texto, log e afins).
- [x] CLI: `rastro analyze <solution|csproj> <Tipo.Metodo> [--out arquivo] [--depth N]`. O `MSBuildLocator` fica só no `Rastro.Cli` (com `Microsoft.Build.Framework` sem assets de runtime).
- [x] Testes EXT-FLOW-001 a 015 sobre a fixture `Fixtures/Alerts` (+ `Alerts.Tests` com um fake), carregada em workspace em memória. Em vez de JSON "golden", asserções explícitas por cenário mais um teste de determinismo (duas extrações geram o mesmo JSON).

**Portões:** backend build 0 erros, 25/25 testes; frontend build ok.

**Checkpoint (avaliado por mim, por decisão do dono de não parar):** job de alertas do repositório A, carregado em ~40 s.
- Primeira rodada: 22 nós, 2 não resolvidos falsos (fakes dos testes do repositório A contando como implementação), `break` sumindo e avisos de auditoria do NuGet como falha. Os três foram corrigidos na própria fase.
- Rodada final: **58 nós, 62 arestas, 0 não resolvidos**. Conferido contra o código: try/catch com `throw`, retorno antecipado, laço com `break` e `continue`, configurações (cache, repositório, descriptografia), envio pelo `NotificationService` até o `HttpClient.PostAsync` e a marcação de enviado no banco.
- [x] Nó externo não tem localização (é compartilhado entre chamadas). A posição da chamada pode ir para a aresta numa fase futura, se fizer falta na tela. Feito na Fase 13.
- [x] `IServiceProvider.GetRequiredService` (service locator) aparece como externo comum. Pode virar destaque nas sugestões de melhoria (Fase 10). **Feito:** virou o achado `ServiceLocator` da Fase 10.

## Fase 2: Persistência e API `[x]`

**Entrega:** levantamentos salvos e consultáveis.

**Status: concluída em 2026-10-15.**
- [x] `Rastro.Core/Surveys/Survey` (status Pending, Running, Completed, Failed; commit, branch, grafo em JSON, contagens) e `Rastro.Infrastructure` (EF Core + Npgsql, snake_case, grafo em `jsonb`, migration `Inicial`, aplicada ao subir).
- [x] Análise em segundo plano: `SurveyQueue` (canal em memória, um por vez) e `SurveyWorker` (ao subir, o que estava rodando vira falha e o pendente volta para a fila; erro de análise vira mensagem para o usuário, erro inesperado vai para o log).
- [x] Endpoints: `POST /api/surveys` (202), `GET /api/surveys`, `GET /api/surveys/{id}` (com o grafo), `GET /api/surveys/{id}/stale`, `GET /api/repositories` e `GET /api/repositories/{nome}/solutions`. Erros 400 com `code` estável (`REPOSITORY_NOT_ALLOWED`, `SOLUTION_PATH_INVALID`, `SOLUTION_NOT_FOUND`, `ENTRY_POINT_REQUIRED`, `DEPTH_OUT_OF_RANGE`).
- [x] Segurança de caminho: só repositórios da configuração, solution relativa e canonicalizada dentro da raiz (sem `..` nem caminho absoluto), só `.sln`/`.slnx`/`.csproj`; a API nunca devolve caminho do disco. Git chamado sem shell e com timeout.
- [x] Logs estruturados sem código-fonte; métricas `rastro_surveys_total{result,reason}` e `rastro_survey_duration_seconds`.
- [x] Docker: a imagem da API passou a ser o SDK (o MSBuild precisa dele); repositórios do host montados só leitura em `/repos`; a análise copia para `/work` e roda o `dotnet restore` lá (cache do NuGet em volume). Local: user-secrets para conexão e repositórios.
- [x] Testes de integração com Postgres real (Testcontainers) e repositório git temporário: API-SRV-001 a 010 e API-REP-001 a 003.

**Portões:** backend build 0 erros, 46/46 testes; frontend build ok. Docker: levantamento real do job de alertas do repositório A dentro do container, 58 nós e 0 não resolvidos, igual à análise local.

## Fase 3: Visualização só de leitura `[x]`

**Entrega:** ver o fluxo no navegador.

**Status: concluída em 2026-10-15.**
- [x] Design system próprio (`docs/frontend/design-system.md`): papel frio, tinta ardósia, âmbar só para a trilha; claro e escuro; Instrument Sans + JetBrains Mono empacotadas no build.
- [x] Lista de levantamentos com polling e formulário (repositório, solution sugerida pela API, ponto de entrada, profundidade) com validação por campo.
- [x] Tela do fluxo: grafo com Foblex Flow + ELK (esquerda para a direita), cores e traços por confiança, não resolvidos em destaque e botão para percorrê-los, minimapa, zoom, legenda.
- [x] **Trilha:** ao escolher um nó, o caminho da entrada até ele acende em âmbar e o resto apaga.
- [x] Painel do nó: tipo, símbolo, `arquivo:linha` com copiar, condição, motivo do não resolvido, ligações (vem de / segue para com confiança e origem), caminho desde a entrada e o trecho do código com a linha destacada.
- [x] Backend: `GET /api/surveys/{id}/source` (trecho do código, só `.cs` dentro do repositório) com testes API-SRC-001 a 003.
- [x] Aviso de "o código mudou" com "Levantar de novo".
- [x] Levantamento de UX feito (`docs/frontend/plano-ux-2026-10-15.md`): 7 achados da revisão visual e 14 do agente aplicados.
- [x] UX-07 ELK em Web Worker, UX-08 não resolvidos no minimapa e UX-16 indicador da IA: **feitos** (UX-16 na Fase 5; UX-07 e UX-08 no plano de UX de 2026-10-16).
- [x] UX-02 andar entre os passos com as setas: feito na Fase 13.
- [x] **Aguardando aprovação do dono (alto risco):** UX-18 copy, UX-19 reordenar o painel, UX-20 legenda recolhível. **Feito:** aprovados e executados em 2026-10-16 (UX-18 como texto real do painel, UX-19 pelo UX-34, UX-20 pelo UX-66).
- [x] Prints em `docs/telas/fase-03/`.

**Portões:** backend build 0 erros, 53/53 testes; frontend build sem avisos, 14/14 testes; verificado no Chrome com o fluxo real do repositório A (claro e escuro).

**Checkpoint go/no-go (avaliado por mim, por decisão do dono de não parar):** **go**. O fluxo do job de alertas aparece completo e legível, a trilha responde à pergunta "como chego aqui?" e o painel liga cada nó ao código. Nenhum bloqueio para seguir com a IA.

## Fase 4: Modo agente: servidor MCP `[x]`

**Entrega:** um agente (Claude Code, por exemplo) usa o Rastro para analisar um código e completar os buracos, sem chave de API.

**Status: concluída em 2026-10-15.**
- [x] Serviços compartilhados movidos da API para `Rastro.Infrastructure` (catálogo, git, processo, analisador) com registro único `AddRastroCore`, usado pela API e pelo MCP.
- [x] `ISolutionSource` com cache de 10 min (uma carga por vez); análise nova sempre lê o código atual.
- [x] Núcleo sem IA: `GraphEditor` (ramo inferido, decisão do usuário com poda do que fica inalcançável, regra do nó), `NodeRule` com origem, `SurveyProposal` (histórico de propostas aceitas, recusadas, confirmadas e descartadas; migrations `Propostas` e `PropostaQuando`).
- [x] `ProofValidator` (Roslyn): aceita só método com corpo na solution cujo `arquivo:linha` cai dentro da declaração; busca de métodos para o agente montar a prova.
- [x] `ProposalService`: porta única para propostas (agente agora, provedor na Fase 5). Recusa nó que não é buraco, levantamento desatualizado (commit mudou) e destino repetido. A prova aceita acrescenta o rastro a partir do destino.
- [x] `Rastro.Mcp` (stdio, SDK oficial 2.2): `list_repositories`, `analyze_flow`, `list_surveys`, `get_flow` (sumário com apelidos n1, n2...), `list_unresolved`, `get_node_code`, `search_methods`, `propose_edge` e `describe_node`, com instruções de uso para o agente. **Sem ferramenta de confirmar:** decisão é do usuário.
- [x] **Adiantado da Fase 6, a pedido do dono:** `describe_node`, para o agente escrever a regra do nó; o painel mostra a regra com a origem.
- [x] **Ajuste descoberto com dado real:** um buraco aceita vários destinos (despacho por tipo de evento no billing do repositório A), cada um com "quando" no rótulo da aresta.
- [x] API `POST /api/surveys/{id}/edges/decision` e tela com Confirmar/Descartar nas arestas inferidas, origem da proposta e buraco "aguardando você".
- [x] **Pedido do dono:** botão para esconder ou mostrar as chamadas do .NET (`System.*`/`Microsoft.*`), escondidas por padrão. Painel mais largo (460 px).
- [x] Zoom inicial calculado pelo layout (o `getScale()` logo após o `fitToScreen` devolvia o valor antigo).
- [x] Testes: CORE-GE-001 a 005, EXT-PROOF-001 a 006, MCP-TOOL-001 a 008, API-DEC-001 a 003 (Testcontainers + fixture). Front: visibilidade das chamadas do .NET.
- [x] Verificado de ponta a ponta com o servidor MCP real por stdio: regras escritas no job de alertas, buraco por reflection no `EventDispatcher` completado com os 11 handlers (uma prova errada recusada de propósito), confirmação feita na tela.
- [x] Abrir em modo legível centraliza a entrada no meio da tela; melhor seria a entrada à esquerda (o canvas do Foblex não tem "pan" direto). Feito na Fase 13.
- [x] UX-08 marcar não resolvidos no minimapa e UX-07 ELK em Web Worker continuam pendentes (grafo de 281 nós já desenha, mas trava a tela por um instante). **Feito:** ELK em Web Worker e não resolvidos (e trilha) no minimapa, Fase C do plano de UX de 2026-10-16.

**Portões:** backend build 0 erros, 80/80 testes; frontend build sem avisos, 16/16 testes. Prints em `docs/telas/fase-04/`.

## Fase 5: Modo provedor: IA configurável `[x]`

**Entrega:** o Rastro completa os buracos sozinho, com o provedor escolhido.

**Status: concluída em 2026-10-15.**
- [x] Projeto `Rastro.Ai` isolado (ADR-001): `AiDbContext` próprio no mesmo Postgres (tabela de histórico `__ai_migrations_history`, migration `IaInicial`), registrado só pela API. Teste de arquitetura novo: nem `Rastro.Infrastructure` nem `Rastro.Mcp` dependem dele.
- [x] Padrão do `meu-portfolio`: `AiProvider` (Claude, OpenAI, Gemini), `AiSettings` (linha única com flag geral, provedor, chave cifrada e modelo por provedor, orçamento diário e uma flag por uso) e `IAiTextService` com uma implementação por provedor (HTTP direto).
- [x] Claude no formato atual da API: saída estruturada por `output_config.format` (o `tool_choice` forçado dá 400 nos modelos atuais), `fallbacks: "default"` com o beta `server-side-fallback-2026-07-01` nos modelos que aceitam, `refusal` e `max_tokens` tratados antes de ler o conteúdo, cache no bloco de sistema. Modelo padrão `claude-opus-5-5`, editável.
- [x] OpenAI com `json_schema` estrito; Gemini com `responseJsonSchema` e a chave no cabeçalho (nunca na URL).
- [x] Chaves cifradas com DataProtection, com as chaves de cifra no banco (sobrevivem ao Docker). A chave nunca volta pela API nem vai para log; o log leva só tokens e um hash curto do pedido. Métricas `rastro_ai_calls_total` e `rastro_ai_tokens_total` (provedor, uso, resultado).
- [x] `AiGateway`: confere flag geral, flag do uso, chave e orçamento antes de gastar; registra o uso do dia.
- [x] `HoleFiller`: a IA vê só o buraco, o código em volta como dado delimitado (com ordem para ignorar instruções dentro dele) e candidatos achados pelo compilador (nome chamado, tipos do motivo, nomes entre aspas no código). Cada resposta passa pelo `ProposalService` e pelo validador de prova, como no modo agente.
- [x] API: `GET/PUT /api/settings/ai`, `GET /api/ai/status`, `POST /api/surveys/{id}/ai/fill-hole` (409 desligada/sem chave/uso desligado, 429 orçamento, 502 falha do provedor).
- [x] Tela **Configurações** (feature flags): modo agente com o comando do MCP para copiar, modo provedor com os três provedores, usos e orçamento com o gasto de hoje. Barra superior com o estado da IA (UX-16). Botão "Propor destinos com IA" no buraco só com IA ligada, configurada e uso habilitado.
- [x] Testes: AI-HTTP-001 a 005 (pedido e resposta de cada provedor, sem rede), AI-API-001 a 006 (chave nunca sai e fica cifrada, bloqueios, alucinação recusada, orçamento, falha do provedor), arquitetura; front: regra do "sem botão morto".
- [ ] **Não verificado contra as APIs reais:** sem chave de API nesta máquina. Os testes cobrem o formato de pedido e resposta; o primeiro uso real (principalmente o `responseJsonSchema` do Gemini) precisa ser conferido com uma chave. Foi para a Fase 13.

**Portões:** backend build 0 erros, 97/97 testes; frontend build sem avisos, 17/17 testes. Prints em `docs/telas/fase-05/`.

## Fase 6: Regras em linguagem humana `[x]`

**Status: concluída em 2026-10-16.**
- [x] Modo agente adiantado na Fase 4 (`describe_node`, regra com origem no painel).
- [x] Modo provedor: `RuleWriter` escreve a regra de métodos, decisões, laços e tratamentos de erro em lotes de até 12 nós (até 36 por pedido), com o código como dado delimitado e um identificador por nó (r1, r2...). Regra para nó não pedido é ignorada.
- [x] Cache pelo hash do trecho de código (`ai_rule_cache`, migration `CacheDeRegras`): mesmo código reaproveita a regra sem chamar a IA.
- [x] **Bug achado pelos testes e corrigido:** com o uso desligado, o cache ainda aplicava regras. Agora o portão (`AiGateway.EnsureAllowedAsync`) é conferido antes de cache ou chamada, nos dois usos (regras e buracos).
- [x] API `POST /api/surveys/{id}/ai/describe` (nós escolhidos ou todos os que faltam; sobrescrever opcional).
- [x] Tela: "Escrever regra com IA"/"Reescrever com IA" no nó e "Escrever as regras que faltam com IA" na visão geral (só com IA ligada, configurada e uso habilitado). **Linha do tempo "Regras do fluxo"** na ordem de execução.
- [x] **Pedidos do dono no meio da fase:** arrastar o canvas não desmarca mais o nó (só o clique sem arraste no fundo, o Esc ou o Fechar limpam); seletor de paletas na barra (Papel, Carta náutica, Planta técnica, cada uma em claro e escuro).
- [x] Testes: AI-RULE-001 a 004; front: ordem da linha do tempo.
- [ ] Dono escolher a paleta definitiva (as nove propostas ficam no seletor até lá). Foi para a Fase 13.

**Portões:** backend build 0 erros, 101/101 testes; frontend build sem avisos (orçamento de CSS por componente subiu para 6 kB, decisão registrada no design system), 18/18 testes. Prints em `docs/telas/fase-06/`.

## Fase 7: Edição visual `[x]`

**Status: concluída em 2026-10-16.**
- [x] Núcleo: `ChangeKind` (novo, sai, muda) e `Intent` em nós e arestas, só no rascunho; `DraftValidator` confere o rascunho contra o extraído (passo do código nunca some nem muda de nome: só é marcado; passo novo com id `new:`, nome e intenção; ligação do código só marcada; ligação nova só entre passos que existem).
- [x] `SurveyDraft` (um por levantamento, `jsonb`, migration `Rascunhos`) e API `GET/PUT/DELETE /api/surveys/{id}/draft` (400 `DRAFT_INVALID` com a lista de erros).
- [x] Front: edição como funções puras (`draft-editor.ts`: novo passo, ligar, remover ligação, mover/religar, remover e restaurar passo, intenção) com desfazer e refazer por pilha (Ctrl+Z, Ctrl+Shift+Z, Ctrl+Y).
- [x] Modo "Editar fluxo": portas visíveis; **ligar arrastando** de uma porta a outra (Foblex `fCreateConnection`); **mover arrastando a ponta** da ligação (`fReassignConnection`); barra com novo passo, desfazer, refazer, contagem de mudanças, salvar, descartar e sair (sair salva o pendente).
- [x] Painel no modo edição: intenção do passo ("o que deve mudar" ou "o que deve fazer"), remover ou restaurar passo, "Seguir para outro passo" (alternativa de teclado ao arraste) e "Remover ligação" em cada ligação.
- [x] Visual: selos "Passo novo", "Muda" e "Sai do fluxo"; passo novo tracejado; passo que sai riscado e apagado; ligação nova tracejada em tinta; ligação que sai apagada. Re-layout sem piscar nem pular a visão.
- [x] Testes: CORE-DRAFT-001 a 004, API-DRAFT-001 a 003; front: operações do rascunho (6 cenários).
- [x] Verificado no Chrome: criar passo, ligar arrastando, remover passo, desfazer/refazer com teclas reais e salvar rascunho validado pelo servidor.
- [x] Rótulo do passo com selo de mudança fica curto demais (o dono do método some); ajustar no levantamento de UX do fim. **Feito:** marcas foram para o canto do nó (UX-53). O corte que sobra, com nome de método longo, vai para a Fase 13.

**Portões:** backend build 0 erros, 108/108 testes; frontend build sem avisos, 24/24 testes. Prints em `docs/telas/fase-07/`.

## Fase 8: Geração do prompt `[x]`

**Status: concluída em 2026-10-16.**
- [x] `DraftDiff` (núcleo, sem IA): passos novos (com quem chama e o que vem depois), modificados, que saem, **movidos** (ligação do código que sai + ligação nova que chega ao mesmo passo), ligações soltas novas e removidas, na ordem do fluxo.
- [x] `ChangePromptBuilder` (núcleo, determinístico): contexto (repositório, solution, commit), o que mudar com âncoras `arquivo:linha` e símbolo, regra atual quando houver, o que não mexer (vizinhos das mudanças), critérios de aceite que o Rastro confere depois (ligações que devem existir e que não devem) e o pedido de validação.
- [x] API `GET /api/surveys/{id}/prompt` e tela: "Gerar prompt" na barra de edição (salva o pendente antes) com janela, copiar e regerar (Esc fecha).
- [x] **Ideia do dono, memória de fluxo:** ferramentas MCP `impact_of` (caminhos da entrada até o passo e o que roda depois) e `get_change_prompt` (o mesmo prompt da tela, como especificação para o agente).
- [x] Contagem de mudanças na barra alinhada ao critério do prompt (passo novo e a ligação dele contam uma; passo movido conta uma).
- [x] Testes: CORE-PROMPT-001 a 004, API-PROMPT-001 e 002, MCP-TOOL-009 e 010; front: contagem.
- [x] Verificado no Chrome com o rascunho real do job de alertas (decisão do chat que muda + passo de auditoria novo).

**Portões:** backend build 0 erros, 116/116 testes; frontend build sem avisos, 25/25 testes. Prints em `docs/telas/fase-08/`.

## Fase 9: Validação pós-alteração e documentação `[x]`

- [x] Validação da mudança (`ChangeValidationService`, comum à API e ao MCP): levanta o fluxo de novo no código atual (vira um levantamento comum, com as regras do original levadas para os passos que continuam), compara com o rascunho e procura mudanças fora do pedido. Sem IA: determinística.
- [x] Critérios conferidos: chamada nova presente (casada pelo id do símbolo ou pelo nome `Tipo.Metodo`), chamada removida ausente, passo alterado ainda no fluxo (a regra nova fica para o usuário conferir).
- [x] Build e testes do repositório por comandos configuráveis (`Rastro:Repositories:N:BuildCommand` e `TestCommand`, sem shell, com tempo limite). Sem comando, aparece "não configurado" e não conta como aprovado. Build quebrado pula os testes.
- [x] **Nota objetiva** (`ChangeValidator.Judge`): percentual de critérios cumpridos; aprovada (tudo cumprido, nada fora do pedido, build e testes sem falha), parcial ou reprovada (build ou testes falharam, ou nenhum critério). Variação dos pontos de decisão (if, laço, try) antes e depois.
- [x] Fila e worker próprios (`ValidationQueue`, `ValidationWorker`), métrica `rastro_validations_total` por veredito, recuperação do que estava rodando ao reiniciar.
- [x] Aprovação do usuário gera a documentação do fluxo em Markdown (`FlowDocumentBuilder`): regras na ordem do fluxo, diagrama Mermaid sem as chamadas do .NET e histórico da mudança com o resultado da validação. Validação reprovada não é aprovada.
- [x] API: `POST /api/surveys/{id}/validations`, `GET /api/surveys/{id}/validations`, `GET /api/validations/{id}`, `POST /api/validations/{id}/approve`, `GET /api/validations/{id}/document`.
- [x] Ferramenta MCP `check_change`: o agente pede a validação depois de alterar e recebe a nota em texto. Aprovar continua sendo do usuário.
- [x] Tela: "Validar mudança" na barra de edição, botão com o resultado da última validação na barra do levantamento, diálogo com nota, critérios, fora do pedido, build e testes (saída do que falhou), aprovar, baixar a documentação e abrir o fluxo novo.
- [x] **Ver somente este fluxo (pedido do dono, 2026-10-16):** botão direito num passo (ou tecla de menu, Shift+F10) abre o menu de contexto; "Ver somente este fluxo" recorta o grafo nos caminhos da entrada até o passo e no que roda depois dele (`focusOn`), sem as ligações laterais. Faixa "Mostrando só o fluxo de X" com "Mostrar tudo", Esc volta. O recorte é enquadrado de novo e centrado no passo.
- [x] Verificado no Docker com o rascunho real do job de alertas do repositório A: build real da solution do repositório A passou (cerca de 3 minutos com restore), validação parcial com nota 50 (o passo de auditoria ainda não existe no código, a decisão do chat continua no fluxo), documentação gerada com as 5 regras escritas pelo agente.
- [x] Feito na Fase 13: o passo novo casado pelo nome confere a assinatura, e os testes do repositório A rodam pelo executor no Windows (`Rastro.Runner`), com Testcontainers.

**Portões:** backend build 0 erros, 129/129 testes; frontend build sem avisos, 31/31 testes. Prints em `docs/telas/fase-09/`.

## Fase 10: Sugestões de melhoria `[x]`

- [x] Métricas do Roslyn por método do fluxo (`QualityInspector`, no extrator, sem IA): complexidade ciclomática, linhas, maior aninhamento e dependências injetadas (só interfaces e serviços contam; construtor de entidade e de record fica de fora). Limiares em `QualityThresholds` (núcleo), iguais para tela, MCP e prompt.
- [x] Heurísticas de código limpo: `catch` vazio (crítico) e `catch` sem log, sem relançar e sem usar a exceção (traduzir a exceção num erro de retorno não conta); service locator (`GetService`/`GetRequiredService` fora da raiz de composição; em job e serviço em segundo plano vira informativo).
- [x] **Regras de DDD (pedido do dono, "trazer à luz as bizarrices"):** entidade alterada fora do domínio (atribuição ou ++ numa propriedade de entidade a partir de controller, job ou serviço de aplicação); gravação da entidade pelo repositório ou `DbSet` fora do domínio (o próprio repositório não conta); setter público nas propriedades da própria entidade (as da classe base de auditoria ficam de fora). "Domínio" é o namespace com o segmento Domain ou Dominio; "entidade" é a classe com Id que mora no domínio ou herda de `*Entity`, `*Document` ou `*Aggregate*`.
- [x] Cada achado ancorado no nó do fluxo (o caminho da entrada até ele é a prova) e na linha exata quando há.
- [x] **Validado no repositório B** (`ApprovalAppService.RegisterDecisionAsync`, 483 nós): 19 achados. Gravação de convite pelo repositório direto da camada de aplicação, `catch (Exception)` que devolve um valor alternativo sem log no `AuditoriaColetor`, service locator no `FeatureFlagProvider`, métodos longos e complexos. O primeiro corte deu 40 achados com ruído (repositório apontado como violador, setter de auditoria da base, construtor de entidade como dependência, catch que traduz a exceção); as regras foram corrigidas com o código real.
- [x] Prompt de refatoração por achado (`RefactorPromptBuilder`, determinístico): problema, local, métricas, caminho do fluxo, o que fazer, o que não mexer e critérios que o Rastro confere. API `GET /api/surveys/{id}/findings/{index}/prompt`; MCP `list_findings` e `get_refactor_prompt`.
- [x] Tela: lista "Melhorias e dívida técnica" na visão geral (DDD primeiro, depois código limpo e métricas), métricas e achados no painel do passo, marca com a contagem e a pior severidade no nó, prompt de refatoração no mesmo diálogo do prompt da mudança.
- [x] **Arrastar os passos (pedido do dono, 2026-10-16):** cada passo pode ser arrastado no fluxo completo e no recorte do "ver somente este fluxo"; as ligações acompanham o passo. A posição fica guardada no navegador por levantamento e por recorte, e "Reorganizar" volta à organização automática.
- [x] Levantamento de UX desta fase: fica junto do levantamento completo no fim do plano (a Fase 11 também mexe na tela).
- [x] A IA sugerir o refactor em cima das métricas (uso "Sugestões" do modo provedor): fica para depois; hoje o prompt determinístico já vai para o agente. Foi para a Fase 13. Feito na Fase 13.
- [x] Regra cruzada entre fluxos ("este fluxo grava a entidade sem a validação que os outros fluxos dela usam"): precisa de mais de um levantamento por entidade; fica para depois. Foi para a Fase 13. Feito na Fase 13.

**Portões:** backend build 0 erros, 137/137 testes; frontend build sem avisos, 34/34 testes. Prints em `docs/telas/fase-10/`.

## Fase 11: Exportação C4 (Structurizr) `[x]`

- [x] Gerador no núcleo, sem IA (`C4WorkspaceBuilder`): Structurizr DSL (`workspace.dsl`) a partir do grafo do fluxo, determinístico.
  - **C4 (código):** visão dinâmica do container da entrada com a ordem numerada das chamadas do fluxo.
  - **C3 (componentes):** os tipos do fluxo, por projeto, com quem chama quem (os nós de estrutura são atravessados).
  - **C2 (containers):** os projetos do fluxo (o extrator agora grava o assembly de cada método) e o banco, quando o fluxo chama EF Core, Npgsql, Dapper ou passa por DbContext ou repositório.
  - **C1 (contexto):** sistemas externos detectados pelas chamadas (HTTP e SDK de terceiro; chamadas do próprio .NET ficam de fora) e as pessoas que o usuário cadastra na tela.
- [x] Pessoas do C1 salvas junto com o levantamento (`SurveyArchitecture`, tabela própria): nome obrigatório e sem repetir, até 10.
- [x] API `GET /api/surveys/{id}/c4`, `PUT /api/surveys/{id}/c4/people`, `GET /api/surveys/{id}/c4/workspace.dsl`; MCP `get_c4`; CLI `rastro c4 fluxo.json`.
- [x] Tela: botão "Diagramas C4" no levantamento, com as pessoas, a prévia do DSL, copiar, baixar e o comando para abrir no Structurizr local.
- [x] Teste golden do DSL da fixture Alerts. O DSL passou no `validate` do Structurizr oficial (`structurizr/structurizr`), e o validador reprova um DSL quebrado de propósito.
- [x] Verificado no repositório B real (`ApprovalAppService.RegisterDecisionAsync`): DSL válido e renderizado no Structurizr local (C1 com Cliente e Vendedor, C2 com 17 projetos, banco e serviço HTTP externo, C4 dinâmico com 10 passos numerados).
- [x] C2 a partir do `docker-compose` (o que roda separado de verdade): hoje cada projeto vira um container, e num monólito modular o C2 fica cheio. Fica para depois. Foi para a Fase 13. Feito na Fase 13.
- [x] A IA sugerir as pessoas e os papéis do C1 (marcado como inferido): fica para depois. Foi para a Fase 13. Feito na Fase 13.

**Portões:** backend build 0 erros, 143/143 testes; frontend build sem avisos, 36/36 testes. Prints em `docs/telas/fase-11/`.

---

## Fase 12: Leitura e navegação do grafo `[x]`

**Status: concluída em 2026-10-16** (o dono pediu todos os itens, mais o ícone da aba e o layout vertical).

**Levantamento de UX da fase:** [`plano-ux-2026-10-16-fase12.md`](../frontend/plano-ux-2026-10-16-fase12.md), 40 achados. Corrigido junto o defeito dos menus suspensos que não recebiam clique do mouse (desde a Fase D). Fases E (baixo risco), F (médio) e G (alto, aprovada pelo dono) executadas.
- [x] Fase G do levantamento de UX da Fase 12.

Nasceu do "Ver somente este fluxo" (Fase 9): o menu do botão direito vira o lugar das ações sobre um passo. Sugestões, em ordem de valor:

- [x] **Buscar no grafo (Ctrl+F):** digita parte do nome, os passos que batem acendem e Enter pula de um para o outro. Em fluxo de 280 nós é o que mais falta.
  Feito: `core/graph/search.ts` (sem acento, todos os termos, nome e símbolo); Ctrl+F ou o botão ⌕ abre a faixa de busca; contorno nos passos que batem e mais forte no atual; "N de M", Enter e Shift+Enter dão a volta; os achados aparecem no minimapa. Verificado no repositório B: "convite" acha 15 passos.
- [x] **Recolher e expandir a partir de um passo:** esconde tudo o que roda depois dele num nó só ("+34 passos"), para ler o fluxo por camadas.
  Feito: `core/graph/collapse.ts` some só o que se alcança passando pelo passo (o que também vem por outro caminho fica); recolher dentro de recolhido some junto; menu do botão direito "Recolher/Expandir o que vem depois", clicar ou Enter no "+N passos" expande, faixa "Expandir tudo", centraliza no passo depois do novo layout; a edição não liga nada ao nó recolhido. Corrigido junto: focar um passo fora da tela rolava o contêiner do grafo (`overflow: hidden`) e descolava a imagem do Foblex; agora o foco não rola e o contêiner volta a zero. Verificado no repositório B: 123 passos atrás do `SaveChangesAsync`.
- [x] **Caminho entre dois passos:** escolhe A e depois B, o grafo destaca só os caminhos de A até B (útil para "como esse job chega no Telegram").
  Feito: `core/graph/between.ts` (passos alcançados a partir de A que também alcançam B, mais o caminho mais curto para o painel); menu "Caminhos a partir daqui", o próximo clique (ou "Caminhos de A até aqui" no menu) escolhe B; faixa com a contagem ou "não é alcançado"; Esc limpa; o painel diz "Caminho a partir de A". Verificado no job de alertas.
- [x] **Comparar antes e depois:** abre o levantamento novo da validação com o original por cima, passos e ligações novas em uma cor e as que sumiram em outra.
  Feito: `core/graph/compare.ts` junta os dois levantamentos pelo id do passo (vem do símbolo); a página aceita `?comparar=<id>` e desenha o antigo por cima: novo em azul, o que saiu em vermelho tracejado e riscado, com a contagem pelo que está na tela. Portas: "Comparar com o original" no diálogo da validação e "Comparar com…", que lista os outros levantamentos prontos do mesmo método. Verificado no job de alertas: profundidade 15 contra 3 dá 9 passos novos e 1 que saiu (o limite de profundidade).
- [x] **Abrir no editor:** do menu, abre o arquivo na linha no VS Code (`vscode://file/...`) ou copia `arquivo:linha` e o nome do método.
  Feito: a pasta de cada repositório nesta máquina é informada em Configurações e fica só no navegador (`core/editor-links.ts`), porque a API nunca manda caminho de disco (no Docker nem conhece o do Windows). Com ela, "Abrir no VS Code" no painel e no menu monta `vscode://file/<pasta>/<arquivo>:<linha>` (caminho que sobe de pasta é recusado) e abre pelo navegador, sem `[href]` nem bypass do sanitizador; "Copiar arquivo:linha e método" no menu. Sem a pasta, o painel diz onde configurar.
- [x] **Exportar o recorte:** baixa o que está na tela (inteiro ou recortado) como imagem e como Mermaid, para colar em documento ou PR.
  Feito: `core/graph/export.ts` gera o Mermaid (direção da tela, forma por tipo, inferida tracejada, confirmada grossa, texto escapado) e o SVG com as posições atuais (inclusive arrastadas) e as cores da paleta; o PNG sai do SVG em até 2x, reduzido se o grafo passar do limite de canvas do navegador. Menu "Exportar": PNG, SVG, copiar Mermaid (em bloco ```mermaid) e baixar .mmd; o nome do arquivo marca "-recorte" quando há foco.
- [x] **Anotação do usuário no passo:** uma nota curta sua num nó ("cuidado: chamado também pelo webhook"), que aparece no painel e na documentação.
  Feito: entidade `NodeNote` (repositório + passo, até 500 caracteres, índice único) e migration `AnotacoesDosPassos`; `GET/PUT api/surveys/{id}/notes` (texto vazio apaga; passo fora do fluxo ou texto longo dá `NOTE_INVALID`); como o id do passo vem do símbolo, a nota reaparece no levantamento seguinte e em outro fluxo que passe pelo mesmo método. Painel com "Anotar este passo", marca ✎ no nó e seção "Anotações" na documentação gerada na aprovação da validação. Cenários API-NOTE-001 a 003 e CORE-VAL-009.
- [x] **Ícone da aba com o logo do Rastro** (pedido do dono em 2026-10-16): `favicon.svg` com a mesma trilha tracejada do logo da barra, mais `favicon.ico` (16, 32 e 48 px) e `apple-touch-icon.png` gerados do mesmo desenho.
- [x] **Layout vertical, além do horizontal** (pedido do dono em 2026-10-16): escolher a direção do grafo (da esquerda para a direita ou de cima para baixo), lembrada no navegador.
  Feito: `LayoutDirection` em `core/graph/layout.ts` (ELK `RIGHT` ou `DOWN`); botão ⇊/⇉ na barra do canvas, escolha guardada no navegador; portas em cima e embaixo do nó na vertical; o arranjo feito à mão é guardado separado por direção. Verificado no job de alertas do repositório A.

## Fase 13: o que ficou para depois `[ ]` (em execução desde 2026-10-16, a pedido do dono)

Juntada em 2026-10-16 a partir das pendências das fases anteriores, para nenhuma ficar esquecida no meio do plano. Cada item diz de qual fase veio e se depende do dono. Os itens de IA, regra cruzada e C2 seguem a regra que já existe (a IA só propõe, o usuário confirma) e a heurística mais conservadora, calibrada no repositório A e no repositório B.

**Depende do dono (não dá para fazer sozinho):**
- [ ] **Modo provedor com chave real** (Fase 5): a configuração já existe (tela de Configurações, chave cifrada). Falta só o dono colar uma chave e usar uma vez ("Escrever regra com IA"); a IA não digita chave de API.
- [ ] **Paleta definitiva** (Fase 6): o dono ainda pesquisa e aceita proposta. Feita a **proposta de 3 paletas de aplicação web** (sem verde), Grafite, Cobalto e Vinho, no seletor e com prints em `docs/telas/fase-13/`; depois, a pedido do dono, mais três a partir de referências de aplicações web (Monocromo e azul, Rosa e carvão, Ártico), sem tirar as anteriores; a escolha segue com o dono.
  Proposta feita na Fase 13 (a escolha segue com o dono): três paletas a mais no seletor, com par claro e escuro, sem verde e com contraste conferido (texto secundário de 6,3:1 para cima, linhas de 3,4:1 para cima):
  - **Grafite e índigo:** neutra, de ferramenta de desenvolvimento; trilha índigo, inferida magenta, confirmada azul.
  - **Cobalto e laranja:** papel frio azulado e tinta marinho; trilha laranja, que salta sobre o azul.
  - **Vinho e pedra:** neutro quente; trilha vinho. Ponto fraco: a trilha fica perto do vermelho dos achados críticos.
  Prints em `docs/telas/fase-13/05` a `07`.

**Leitura do grafo (frontend):**
- [x] **Nome do tipo cortado no nó** (Fase 7 e plano de UX): com nome de método longo, o dono vira "A…" porque o nó tem largura fixa (252px). Proposta: largura do nó calculada pelo texto, com teto, antes de passar ao ELK.
  Feito: `nodeSize` calcula a largura pelo nome (7,8px por caractere em mono 13px, mais 52px de padding e glifo), com o mínimo do tipo e teto de 440px; junto, corrigido o padding de 24px que o Foblex impunha ao nó. No repositório B, nomes cortados caíram de 150 para 20 (os 20 passam do teto, de propósito).
- [x] **UX-02, andar entre os passos com as setas** (plano de UX de 2026-10-15): setas levam ao passo seguinte ou anterior pelas ligações, sem o mouse (Alt+setas continua movendo).
  Feito: `core/graph/keyboard.ts` (para a frente pela 1ª ligação, para trás pelo caminho, para os lados entre os irmãos, na ordem de execução); na vertical as direções giram; roving tabindex (uma parada de Tab no grafo, o passo escolhido ou a entrada); o painel acompanha. Verificado com teclas reais no job de alertas.
- [x] **Abrir com a entrada à esquerda** (Fase 3): no modo legível a entrada abre no meio da tela; o certo é no canto onde a leitura começa.
  Feito: grafo grande abre na escala legível com a entrada a 80px da borda esquerda, na altura do meio (na vertical, a 80px do topo e centralizada); o recorte "ver somente" segue centralizado no passo escolhido. Verificado no repositório B nas duas direções.
- [x] **Nó externo com a posição da chamada** (Fase 1): o nó externo é compartilhado entre chamadas e não tem arquivo e linha; a posição pode ir para a ligação e aparecer no painel.
  Feito: `FlowEdge.Location` (opcional, schema compatível) com o arquivo e a linha da chamada, preenchido pelo extrator na ligação que chega ao nó externo; no painel do externo, o bloco do arquivo diz "Chamado em" (com abrir no VS Code, copiar e o trecho do código) e cada ligação que chega mostra onde acontece. Levantamentos antigos não têm a posição; "Levantar de novo" traz. Cenário EXT-FLOW-016.

**Validação da mudança:**
- [x] **Passo novo conferido pela assinatura** (Fase 9): hoje o passo novo do rascunho casa com o levantado só pelo nome.
  Feito: `StepSignature` casa "Tipo.Metodo" pelo nome e "Tipo.Metodo(string, int)" também pelos parâmetros do símbolo (último segmento do tipo, alias como string = String, genéricos e nome de parâmetro ignorado); com mais de um candidato, vence o chamado pelo passo de origem. A tela de novo passo explica como escrever os tipos. Cenários CORE-VAL-010 a 012.
- [x] **Testes do repositório A na validação** (Fase 9): o comando de testes precisa de Docker (Testcontainers) e o container da API não cria containers. **Decisão do dono (2026-10-16): executor no Windows (`rastro-runner`).** A API no container pede, o executor fora do Docker roda só os `BuildCommand` e `TestCommand` da configuração, sem shell, com timeout, escutando em `127.0.0.1` com chave compartilhada. Recusados: montar o socket do Docker e Docker dentro do Docker (dão ao container poder de administrador da máquina).
  Feito: projeto `Rastro.Runner` (só `127.0.0.1:7420`, chave de 24+ caracteres comparada em tempo constante, um comando por vez, sem shell, com timeout); o pedido diz só o repositório e se é build ou teste, e a linha de comando, a pasta e as variáveis de ambiente (`Environment`, ex.: `TestContainers=true` do repositório A) ficam na configuração do executor (user-secrets). A API usa o executor quando `Rastro:Runner:Url` está preenchido (no Docker, `RUNNER_URL=http://host.docker.internal:7420` e `RUNNER_KEY` no `docker/.env`); sem ele, roda local como antes; executor fora do ar vira resultado "Suba o executor e valide de novo". Verificado: de dentro do container, build do repositório A com código 0; pelo executor, a suíte inteira do repositório A com o Postgres do Testcontainers (1862 aprovados, 0 falhas, 1 ignorado, 3 min 44 s). Para usar: `dotnet run --project backend/src/Rastro.Runner` antes de validar. Cenários RUNNER-001 a 007.

**Qualidade e arquitetura:**
- [x] **IA sugere o refactor sobre as métricas** (Fase 10): uso "Sugestões" do modo provedor; hoje vai o prompt determinístico para o agente.
  Feito: `RefactorAdvisor` (uso "Sugestões"): código do método como dado delimitado, resposta em formato fixo (resumo e até 6 passos), orçamento e flag conferidos antes; no diálogo do prompt de refatoração, "Pedir sugestão de refactor à IA" mostra a sugestão marcada como da IA e "Acrescentar ao prompt". Não muda o grafo. Cenários AI-SUG-001 e 002.
- [x] **Regra cruzada entre fluxos** (Fase 10): "este fluxo grava a entidade sem a validação que os outros fluxos dela usam"; precisa de mais de um levantamento por entidade.
  Feito: `CrossFlowInspector` compara o levantamento com o mais recente de cada outro ponto de entrada do repositório: passo que grava (repositório ou store com método de escrita), entidade pelo tipo do parâmetro, guardas com nome de checagem no caminho; achado só quando 2 ou mais outros fluxos usam a guarda que este pula. `GET api/surveys/{id}/cross-flow` e seção "Entre fluxos" na visão geral. Cenários CORE-CROSS-001 a 003 e API-CROSS-001.
- [x] **C2 a partir do `docker-compose`** (Fase 11): o que roda separado de verdade, em vez de um container por projeto.
  Feito: `DeploymentReader` lê o compose do repositório (raiz, `docker/` ou `deploy/`): serviço com `build` vira container com o projeto do Dockerfile (o `.csproj` da pasta ou o citado nele) e as referências; serviço de banco dá nome ao banco; arquivo fora da raiz é ignorado. Sem compose, o C2 continua por projeto (golden igual). Vale para a tela, o download e o `get_c4` do MCP. Cenários C4-DEPLOY-001 a 003.
- [x] **IA sugere as pessoas do C1** (Fase 11), marcadas como inferidas para o usuário confirmar.
  Feito: `ContextAdvisor` (uso "Contexto do C4"): nomes do ponto de entrada, das classes e dos sistemas externos como dado; até 5 pessoas, sem repetir as cadastradas; no diálogo do C4, "Sugerir pessoas com IA" e "Acrescentar" por pessoa (vale depois de salvar). Cenário AI-C1-001.

**Levantamento de UX da Fase 13** (2026-10-16, 15 achados, UX-140 a UX-154):
- [x] Risco baixo e médio, aplicados: setas não viram destino de "Caminhos a partir daqui" (UX-140); seta sem para onde ir é anunciada e o nó descreve as teclas ao leitor de tela (UX-141, UX-142); excluir também o levantamento que falhou (UX-143); excluir com "Excluindo…" e aviso na lista (UX-144); sugestão acrescentada ao prompt vira aviso e o foco vai ao prompt (UX-145); C4 diz quando todas as sugestões foram acrescentadas, anuncia o resultado e mostra onde ligar a IA (UX-146 a UX-148); ligações que saem mostram arquivo:linha e "Chamado em" diz quantas chamadas há (UX-152); paletas sem colisão entre trilha, não resolvido e erro (UX-153, UX-154); o trecho de código espera a seleção parar antes de ser pedido.
- [x] UX-149: a pessoa do C1 que veio da IA guarda a origem ao salvar, mostra o selo "IA" até ser editada e sai no DSL com a tag "Inferida" (borda tracejada). Cenários API-C4-004 e AI-C1-001.
- [x] **Aprovados pelo dono e aplicados:** UX-150, padronizar os rótulos destrutivos ("Tirar" vira "Remover"; "Apagar anotação" vira "Excluir anotação"); UX-151, trocar "refactor" por "refatoração" no botão e reescrever o texto de "Entre fluxos" ("salva X sem a verificação Y, que os fluxos Z fazem antes de salvar"). "Apagar a chave salva" nas Configurações também virou "Excluir a chave salva".

**Dados locais:**
- [x] **Excluir levantamento** (governança da Fase 12): não havia como apagar um levantamento pela tela nem pela API.
  Feito: `DELETE api/surveys/{id}` apaga com rascunho, propostas, validações e pessoas do C4 (cascata); as anotações ficam, porque são do repositório e do passo. Recusa levantamento em análise (`SURVEY_RUNNING`) e com validação rodando (`VALIDATION_RUNNING`). Na tela, menu "⋯" com "Excluir levantamento…" e confirmação; volta à lista. Os dois levantamentos de teste criados nesta fase e na 12 foram apagados. Cenários API-SURVEY-DEL-001 e 002.

## Fase 14: produto distribuído como `dotnet tool` `[x]` (pedido do dono em 2026-10-16)

Decisão: [ADR-002](../adr/adr-002-produto-dotnet-tool.md). O Rastro deixa de ser ferramenta interna: o cliente instala com `dotnet tool install -g rastro-ai` e roda `rastro`, sem Docker e sem Postgres. O dono começa usando o pacote e refina a experiência; a página de venda vem depois (Fase 15).

**Status: concluída em 2026-10-16.** Pacote `artifacts/rastro-ai.0.1.0.nupkg` (35 MB) instalado na máquina do dono com `dotnet tool install -g`, com os dados dele migrados e a licença dele ativa.

- [x] **SQLite no lugar do Postgres:** provedor trocado nos dois contextos (núcleo e IA), migrations novas, datas gravadas como número para ordenar e comparar no SQLite, modo WAL ao abrir, testes sem Testcontainers (banco em arquivo temporário).
  Feito: `RastroDatabase` (connection string padrão em `~/.rastro/rastro.db`, `DateTimeOffsetToBinaryConverter` por convenção, `PRAGMA journal_mode=WAL` na API e no MCP); migrations do Postgres substituídas por `Inicial` e `IaInicial`; no Windows as chaves do DataProtection ficam cifradas com o DPAPI do usuário (copiar o `rastro.db` para outra máquina não abre a chave de API). A suíte inteira roda no SQLite.
- [x] **Pasta do usuário** (`~/.rastro`, ou `RASTRO_HOME`): banco e `config.json` com os repositórios; sem connection string configurada, o banco vai para lá. Comandos `rastro repo add|remove|list`.
  Feito: `RastroHome`; o `config.json` entra por último na configuração e recarrega ao mudar, e o `RepositoryCatalog` passou a `IOptionsMonitor` (repositório novo aparece sem reiniciar). `rastro repo add <nome> <pasta> [--build] [--testes]` valida o nome, a pasta e a repetição. Cenários CLI-REPO-001 a 004.
- [x] **Um executável só:** a API vira o pacote (`PackAsTool`, comando `rastro`); a tela Angular vai embutida e é servida pela própria API; `rastro mcp` sobe o servidor MCP (o `Rastro.Mcp` vira biblioteca); porta padrão 7450, só em `127.0.0.1`, com `--porta`.
  Feito: `RastroCommandLine` (`mcp`, `repo`, `licenca`, `--version`, `--help`); sem comando, sobe a tela com a raiz de conteúdo ao lado do executável e o `wwwroot` com fallback para o `index.html` (rota `api/` desconhecida continua 404); o MCP só carrega o user-secrets no desenvolvimento e se apresenta como `rastro`. Terminal em UTF-8. Comando do MCP na tela de Configurações: `claude mcp add rastro -- rastro mcp`; os avisos de "nenhum repositório" apontam para o `rastro repo add`.
- [x] **Licença:** chave assinada (ECDsa P-256) com e-mail, emissão e validade; avaliação de 14 dias a partir do primeiro uso; `rastro licenca` mostra a situação e `rastro licenca ativar <chave>` grava. Obrigatória só no build de release. Emissor separado (`Rastro.Licensing.Issuer`, fora do pacote), com a chave privada fora do repositório.
  Feito: `Rastro.Licensing` (`LicenseKey`, `LicenseGate`, chave pública embutida); par de chaves gerado na máquina do dono (`~/.rastro-emissor/chave-privada.pem`); e-mail mascarado no terminal; chave inválida não substitui a gravada. Verificado no pacote: avaliação vencida fecha a tela e o MCP com código 2 e diz como ativar. Cenários LIC-001 a 007.
- [x] **Ofuscação** com o Obfuscar nos assemblies do Rastro dentro do pacote, preservando a API pública; pacote ofuscado instalado e testado de ponta a ponta.
  Feito: Obfuscar 2.2.50 como local tool do repositório; ofusca `Rastro.Core`, `Rastro.Extractor`, `Rastro.Infrastructure` e `Rastro.Licensing` (internos renomeados, strings escondidas, propriedades mantidas por causa do JSON e do EF); API, IA e MCP ficam de fora, porque ASP.NET, JSON e o esquema das ferramentas MCP dependem dos nomes. Verificado no pacote instalado: levantamento do job de alertas do repositório A (58 passos), C4, anotação, configuração da IA com chave, exclusão e o MCP (15 ferramentas, parâmetros com os nomes) com a tela no ar no mesmo banco.
- [x] **Empacotamento:** `scripts/empacotar.ps1` (build do front, pack, ofuscação), README e termos de uso no pacote, versão do `VERSION`.
  Feito: o script troca as DLLs ofuscadas dentro do `.nupkg` e guarda o mapa da ofuscação em `artifacts/` (não se publica); README e termos de uso em `docs/pacote/`; `RASTRO_BIN` no `Directory.Build.props` para compilar com o Visual Studio rodando a API.
- [x] **Docker e executor no Windows aposentados;** dados do dono copiados do Postgres para o SQLite.
  Feito: `docker/`, `Rastro.Runner` e o cliente remoto removidos; o build e os testes da validação rodam pelo `LocalCommandRunner` (cenários VAL-CMD-001 a 003 no lugar dos RUNNER-*). Copiados para o `~/.rastro`: 6 levantamentos, 19 propostas, 1 rascunho, 1 validação, 2 C4 e a configuração da IA com o anel de chaves; repositórios `repositorio-a` (build e testes com `TestContainers=true`) e `repositorio-b` no `config.json`.
- [x] **Docker da versão nova** (pedido do dono em 2026-10-16, depois de aposentar o antigo): imagens, containers e volumes antigos apagados; um container só (`rastro`, API com a tela embutida, SQLite no volume `rastro-data`) em `localhost:7400`, com o código montado só para leitura e cópia de trabalho para o restore. Banco próprio, separado do `~/.rastro` do `rastro` instalado. Verificado com a licença do dono e seis levantamentos (repositório A: `WebhookController.Receive` com 515 passos, job de alertas, `PaymentWebhookController.Handle`; repositório B: `ApprovalAppService.RegisterDecisionAsync` com 480, `ProcessosMudancasController.AvancarEtapa`, `CotacoesController.Gerar`).
- [x] **Documentação:** README (instalar e usar), `visao.md`, `CLAUDE.md` (ambiente) e o comando do MCP na tela de Configurações (`rastro mcp`). Prints em `docs/telas/fase-14/`.

**Depende do dono:**
- [ ] Criar a conta no nuget.org e publicar o pacote (o nome `rastro-ai` precisa estar livre lá).
- [ ] Revisar os termos de uso do pacote.
- [ ] Guardar cópia da chave privada de licença fora da máquina (sem ela, não se emite licença nova).

## Refinos pedidos no uso (desde 2026-10-16)

Ajustes que o dono pede enquanto usa o Rastro, antes da página de venda. Cada um entra com teste e conferido na tela.

- [x] **Recolher o painel da direita** (pedido do dono em 2026-10-16): mais espaço para o grafo.
  Feito: aba ‹/› na borda do painel (no celular, na borda de cima); recolhido, o grafo ocupa a largura toda; a escolha fica guardada no navegador e vale para os outros levantamentos.
- [x] **Seleção múltipla para mover vários passos juntos** (pedido do dono em 2026-10-16): arrastar no fundo do grafo desenha o laço; Ctrl+clique marca e desmarca um passo; os marcados ganham contorno tracejado e se movem juntos, com as posições guardadas como no arraste de um passo só.
  Feito: `f-selection-area` do Foblex com gatilho próprio.
- [x] **Levantamento que falhou volta ao formulário preenchido** (pedido do dono em 2026-10-17): hoje "Voltar e tentar outro ponto de entrada" leva à lista com o formulário vazio, e é preciso escolher repositório, solution e ponto de entrada de novo. O botão passa a ser "Corrigir e levantar de novo" e leva os dados do levantamento que falhou para o formulário, com o foco no ponto de entrada.
  Feito: o pedido vai pelo estado da navegação; a lista escolhe o repositório e a solution, preenche o ponto de entrada e a profundidade e seleciona o ponto de entrada para trocar.
- [x] **Ponto de entrada não encontrado sugere os métodos com esse nome** (achado no mesmo uso): o dono escreveu `Loja.Api.Controllers.Reservar`, sem a classe, e o erro só dizia "não encontrado". O erro passa a listar até 5 métodos com o mesmo nome na solution (`Namespace.Tipo.Metodo`), ou dizer que não há nenhum.
  Feito: `EntryPointResolver` guarda os métodos de mesmo nome que não bateram com o tipo. Cenários EXT-FLOW-017 e 018.
- [x] **Lista de solutions só com `.sln` e `.slnx`** (pedido do dono em 2026-10-17): hoje a lista também traz todos os `.csproj`, e a solution se perde no meio deles. Os `.csproj` só aparecem quando o repositório não tem nenhuma solution. O levantamento continua aceitando `.csproj` (pelo MCP e nos levantamentos antigos).
  Feito: `RepositoryCatalog.Solutions`. Cenários CAT-SOL-001 a 003.
- [x] **Explicação de cada campo do levantamento novo** (pedido do dono em 2026-10-17): não fica claro o que se escreve em "Ponto de entrada", nem para que servem solution e profundidade. Cada campo ganha um botão "i" ao lado do nome, que abre e fecha a explicação com exemplos logo abaixo (com mouse, teclado e leitor de tela; Esc fecha), e o resumo aparece também ao passar o mouse.
  Feito: `.info-toggle` e `.info-text` no tema, para reaproveitar em outras telas; os textos ficam num `Record` por campo.
- [x] **Arrastar no fundo volta a mover a visão** (pedido do dono em 2026-10-17): com a seleção múltipla, arrastar no fundo passou a desenhar o laço, e o dono não conseguia mais arrastar o grafo até um ponto para dar zoom. Arrastar volta a mover a visão; o laço passa para Shift+arrastar, e Ctrl+clique continua marcando um passo.
  Feito: `isSelectionGesture` exige Shift; legenda e ajuda do teclado atualizadas. Verificado no pacote com arraste de verdade no fundo. Cenários SEL-001 a 006. Como arrastar no fundo passou a ser o laço, mover a visão é com Espaço+arrastar ou o botão do meio; a roda continua no zoom. Com vários marcados o painel fica no passo que já estava. Legenda atualizada. Cenários SEL-001 a 005 (`flow-canvas.component.spec.ts`).
- [x] **Abrir o `rastro` com ele já aberto não dá erro** (pedido do dono em 2026-11-02): o segundo `rastro` despejava a pilha de `address already in use` no terminal, e até migrava o banco antes de falhar. Agora ele confere a porta antes de migrar: se quem responde é um Rastro, diz o endereço e sai com sucesso; se é outro programa, sugere `--porta` e sai com erro.
  Feito: `PortProbe`, mais o tratamento de porta tomada entre a conferência e a subida. Cenários CLI-PORT-001 a 007.

## Fase 17: pronto para distribuir `[x]` (pedido do dono em 2026-10-17)

O dono olhou a tela pensando no cliente e achou o que só faz sentido na máquina dele. Decisão do dono (2026-10-17): os repositórios passam a ser cadastrados pela tela ([ADR-003](../adr/adr-003-repositorios-pela-tela.md)).

**Status: concluída em 2026-10-17**, menos o resto da passada no navegador (item em aberto abaixo).

**Configurações e navegação:**
- [x] **Repositórios pela tela** (ADR-003): seção "Repositórios" nas Configurações, com a lista (nome e pasta), adicionar (nome e pasta) e remover com confirmação. O "Abrir no VS Code" passa a usar a pasta cadastrada; some a pasta digitada no navegador. Build e testes da validação continuam só pelo terminal (`rastro repo add --build --testes`), porque comando nunca vem da requisição. A gravação do `config.json` sai do comando `rastro repo` para um serviço da infraestrutura, usado pela CLI e pela API.
  Feito: `RepositoryConfigFile` (único que grava o `config.json`, com trava), usado pelo `rastro repo` e pelo `RepositoriesController` (`POST` e `DELETE api/repositories`; a lista devolve a pasta, se há `.git` e se build e testes estão configurados). Depois de gravar, a API recarrega a configuração na hora, e o levantamento novo já vê o repositório. O `ApiFixture` passou a cadastrar o repositório de teste no `config.json` do `RASTRO_HOME` próprio. Na tela, `EditorLinks` lê a pasta da API. Verificado no pacote: cadastrar e remover uma pasta temporária com clique de verdade. Cenários API-REPO-001 a 013.
- [x] **API protegida contra pedido de outro site** (ADR-003): `AllowedHosts` só com `localhost`, `127.0.0.1` e `[::1]` (contra DNS rebinding) e pedido que muda dado (POST, PUT, PATCH, DELETE) recusado quando o `Origin` não é da própria máquina. Vale para todas as rotas, não só para os repositórios (a validação já roda build e testes).
  Feito: `OriginGuardMiddleware` (403 `ORIGIN_NOT_ALLOWED`, `Origin: null` também recusado, log sem o corpo). Verificado no pacote: `Origin` de fora dá 403, `Host` de fora dá 400, a tela local passa.
- [x] **Engrenagem no cabeçalho** que leva às Configurações, como nos outros sistemas do dono, e **botão "Voltar"** nas Configurações (volta para a tela anterior ou para a lista).
- [x] **Sem dado do dono no produto:** placeholder e mensagens com nomes de repositórios do dono ou caminho da máquina dele trocados por exemplo genérico.
  Feito: ponto de entrada (`PedidosController.Criar`), novo passo, anotação, pessoa do C4, pasta (`C:\codigo\meu-sistema`) e a descrição da ferramenta do MCP. Sem repositório, a lista leva às Configurações em vez de mandar ao terminal e recarregar.

**Defeitos:**
- [x] **Diálogo do C4 cortado à direita:** ao exportar para o Structurizr, o diálogo passa da largura da tela.
  Causa: o `.overlay` é grid e a trilha automática crescia até o tamanho do conteúdo, então o `max-height` do diálogo não valia (1870px de altura numa janela de 911px, rodapé fora da tela). Corrigido no tema com trilhas `minmax(0, 1fr)`, o que vale para todos os diálogos.

**Revisão completa da tela:**
- [ ] Passada no navegador, com clique de verdade, por todas as telas e funcionalidades, corrigindo o que aparecer.
  Feito em 2026-10-17: lista, primeiro uso (`RASTRO_HOME` vazio), grafo, painel, menu "⋯", edição (novo passo, gerar prompt e validar sem mudança), C4 e Configurações, em 1920. Falta: validação com mudança de verdade, sugestões com a IA ligada, anotação, exclusão e a largura 1280 (a janela do Chrome maximizada não encolheu).
- [x] **Skill `levantamento-ux` revisada** com o que se aprendeu: checagem de dado do dono na tela, diálogo com conteúdo real nas duas larguras, caminho de ida e volta de cada tela e clique de verdade.
  Feito: olhar de cliente, primeiro uso com `RASTRO_HOME` vazio, ida e volta, e uma seção de verificação obrigatória no navegador (clique pelo `ref`, nunca por coordenada de print reduzido; diálogo medido contra a janela; console sem erro).
- [x] Prints em `docs/telas/fase-17/` e pacote gerado de novo.

## Fase 18: tronco do método e visão em árvore `[x]` (pedido do dono em 2026-10-17)

O dono olhou o `Reservation.Create` do repositório C: seis validações em sequência (`if` com `throw`) apareciam como seis caminhos paralelos saindo do método, como se cada `if` fosse um método, e o grafo ficava largo e enganava a leitura. Pediu que os comandos de um método formem um tronco reto, na ordem do código, com os ramos saindo como galhos, também no desenho horizontal e no vertical, e um botão para abrir ou fechar o detalhe de cada método. Pediu ainda que a visão "Árvore" fique como opção.

O dado já existia: cada ligação guarda a ordem (`Order`) e o ramo (`Label`: then, else, case, catch, each). A fase é de desenho; o extrator não muda, e nenhuma ramificação se perde.

**Status: concluída em 2026-10-17.** Prints em `docs/telas/fase-18/`.

- [x] **Tronco do método no horizontal e no vertical:** os comandos de um bloco (`if`, `switch`, laço, `try`) saem um do outro, na ordem do `Order`, e não mais todos do método. O `throw` de cada validação sai como galho, com o rótulo do ramo. Chamada de método sai do tronco como passo normal, e o comando seguinte não sai dela (uma seta saindo de um método leria "ele chama o próximo"); `return`, `throw` e desvio encerram o bloco.
  Feito: `trunkSources` (`core/graph/sequence.ts`) decide de onde a linha sai na tela, e o ELK usa as mesmas ligações. A ligação guardada continua do método para o comando: a trilha, a edição e o Mermaid seguem o dado do extrator. O rótulo do ramo aparece só na primeira ligação dele (os passos de um `try` não repetem "try").
- [x] **Visão "Árvore"**, terceira opção no botão que troca o desenho (horizontal, vertical, árvore). Determinística e sem ELK: o tronco do método desce um nível para dentro, na ordem, e os ramos saem para a direita, na linha do passo de onde saem. Ligação em ângulo reto por um desenhista próprio registrado no Foblex (`F_CONNECTION_BUILDERS`), igual ao SVG exportado.
  Feito: `treeLayout` (`core/graph/tree-layout.ts`) e `TreeConnectionBuilder` (`core/graph/tree-connection.ts`); posições arrastadas guardadas por desenho.
- [x] **Abrir e fechar o detalhe do método:** ▾/▸ no passo do método (nos três desenhos), também pelas teclas + e −, usando o recolher que já existia ("+N passos").
- [x] **Defeito achado no caminho:** o efeito que calcula o layout lia os passos já posicionados e, ao gravá-los, rodava de novo sem fim. Com o ELK, o layout era recalculado sem parar em segundo plano; com a árvore, que é instantânea, a tela travava. Corrigido lendo sem rastrear.
- [x] Rótulo de ramo longo (como `catch (Exception)`) fica no meio de uma ligação comprida na árvore; aproximar do passo de destino. Feito na Fase 19 (o `catch` vira seção do bloco, com o rótulo no cabeçalho).
- [ ] Fica para decidir com o dono: o `return` do fim do método e o construtor chamado com `new` (`return new Reservation(...)`) não viram passo hoje. Mostrar mudaria todos os grafos (mais nós) e os testes de referência do extrator.

## Fase 19: ramificação mais legível `[x]` (pedido do dono em 2026-10-17)

O dono achou que a ramificação ainda pesa na leitura. Olhando o `ReservationController.Reserve` do repositório C, cada validação ocupava três peças (a caixa do `if`, a etiqueta "then" e a caixa do `throw`), os erros ficavam espalhados em alturas e colunas diferentes, o corpo do `try` parecia solto e o comando chamava mais atenção que o método. O dono aprovou as cinco mudanças abaixo. A fase é só de desenho: o extrator e o dado guardado não mudam.

**Status: concluída em 2026-10-17.** Prints em `docs/telas/fase-19/`.

- [x] **Validação num passo só:** `if` sem `else` cujo único passo é um `throw`, `return`, `break` ou `continue` vira um passo só, com o desfecho em destaque na ponta (`if (productId == Guid.Empty) → throw new ArgumentException`). Vale para os três desenhos e para o SVG exportado. Fica separado quando o desfecho tem marca própria (mudança do rascunho, achado, anotação) e no modo de edição, para que seja possível ligar nele.
  Feito: `findGuards` e `withGuards` (`core/graph/guards.ts`); `throw new X` aparece como `throw X`, e a validação tem teto de largura maior. Busca, seleção e setas que caem no desfecho escondido vão para a validação.
- [x] **Etiqueta só onde muda o sentido:** `else`, `case`, `catch` e `finally`; o "then" só aparece quando o `if` tem `else`, e o "try" e o "each" somem.
- [x] **Galhos alinhados na árvore:** os galhos dos passos de um mesmo tronco começam na mesma coluna, e as validações em sequência têm a mesma largura, com o desfecho alinhado. O vão do galho cresce com o rótulo (`catch (Exception)` cabe inteiro).
- [x] **`try` e laço como bloco na árvore:** o corpo desce dentro de um bloco com fundo próprio; cada `catch` e o `finally` viram uma seção do mesmo bloco, com o rótulo no cabeçalho.
  Feito: `treeDrawing` devolve os passos e os blocos; o bloco some quando algum passo foi arrastado à mão (o fundo não acompanha o arraste). O SVG exportado não desenha o bloco.
- [x] **Método em destaque, comando leve:** a caixa do método ganha contorno e nome mais fortes; `if`, `switch`, laço e `try` ficam sem fundo e com contorno claro.

## Fase 20: linha do tempo no lugar do desenho vertical `[x]` (pedido do dono em 2026-10-17)

No job de alertas do repositório A, o desenho vertical punha lado a lado passos que acontecem um depois do outro: dentro do `try`, o `GetPendingAlertsAsync`, o `if`, o `MarkFailure` e o `throw` ficavam na mesma linha, como se fossem caminhos alternativos, e o `foreach` parecia sair de dentro do `if`. A causa: só `if`, laço e `try` puxavam o passo seguinte, e o ELK espalhava os irmãos lado a lado. O dono pediu uma linha do tempo misturada com a ramificação e um desenho que um júnior ou uma pessoa não técnica entenda.

**Status: concluída em 2026-10-17.** Prints em `docs/telas/fase-20/`.

- [x] **Linha do tempo:** substitui o desenho vertical. Os passos de um método descem um embaixo do outro, na ordem do código, ligados por uma linha reta ("depois"). O que um método chama, o corpo do `if` e o corpo do laço saem para a direita como galho, e o passo seguinte continua logo abaixo, sem ser empurrado para longe. O `try` vira bloco, com o `catch` como seção. Determinística e sem ELK.
  Feito: `timelineDrawing` e `timelineRoutes` (`core/graph/timeline-layout.ts`). Quem tinha o desenho vertical guardado passa a abrir a linha do tempo.
- [x] **Texto para quem não programa:** na linha do tempo, os comandos aparecem em português: "Se ...", "Para cada ... em ...", "Tenta", "Se der erro", "Para com erro ...", "Encerra e devolve ...", "Sai da repetição", "Pula para o próximo". O código original fica no texto de ajuda do passo, e o nome dos métodos não muda.
- [x] **Cabeçalho do galho:** "Se sim", "Se não", "Caso ..." e "Nos outros casos" ficam escritos em cima do galho, e não no meio da linha.
- [x] **Legenda da linha do tempo:** explica a linha reta ("depois") e a seta para a direita ("chama" ou "entra").
- [x] **Achado no caminho, método chamado em dois lugares:** era um nó só, e as linhas cruzavam a tela até ele (o `MarkSuccess` do job de alertas aparece no `if` e no fim do `try`). Na linha do tempo, a segunda chamada vira um passo repetido (↻, tracejado), no lugar onde acontece; clicar nele leva ao original, onde estão os passos.
- [x] Galho longo empurra para baixo o galho do passo seguinte (todos dividem a mesma coluna), e a seta dele desce bastante. Decisão do dono em 2026-10-17: os métodos chamados continuam abertos; quem quiser fecha pelo ▾.

## Fase 21: a linha do tempo vira a regra de todo desenho `[x]` (pedido do dono em 2026-10-17)

O dono deixou claro que a linha do tempo não é uma visão a mais: é o novo jeito de desenhar o fluxo, e vale para as três visões. Não existe quarta visão. Decisões do dono: horizontal e vertical seguem a regra com ligações curvas; a árvore segue a mesma regra com ligações retas em ângulo; o texto em português vale para todas.

**Status: concluída em 2026-10-17.** Prints em `docs/telas/fase-21/`.

- [x] **Horizontal com a regra nova:** os passos seguem para a direita, na ordem do código, e o que um passo chama (ou faz quando a condição vale) desce como galho. O passo seguinte continua logo ao lado, sem ser empurrado.
  Feito: `flowDrawing` (`core/graph/timeline-layout.ts`) mede tudo no sentido dos passos e no dos galhos, e serve aos dois sentidos. Num fluxo grande, a horizontal vira uma faixa comprida (o espelho da vertical); ela abre legível na entrada.
- [x] **Vertical:** a linha do tempo da Fase 20, com ligações curvas.
- [x] **Árvore:** a mesma regra da vertical, com ligações retas em ângulo.
- [x] **Português em todas as visões**, com os cabeçalhos dos galhos; o código fica no texto de ajuda e no painel do passo.
- [x] **Bloco do `try`** nas três visões.
- [x] **Exportação em SVG** com o mesmo desenho da tela, com o passo repetido e os cabeçalhos dos galhos (o bloco do `try` não vai para o SVG).
- [x] Limpeza: o ELK, o tronco da Fase 18 (`trunkSources`) e o desenho antigo da árvore deixam de ser usados e saem do código, num commit próprio. O que sobrou do arquivo da árvore (rótulos de ramo e o abrir e fechar do método) foi para `core/graph/branches.ts`.

## Fase 23: leitura do fluxo de relance `[x]` (pedido do dono em 2026-10-17)

O dono achou que as três visões ficaram boas, mas ainda bagunçadas para quem bate o olho: todo passo tem o mesmo peso visual e não há por onde começar. Pediu melhorias de cognição e UX para o fluxo se explicar sozinho, também para júnior e pessoa não técnica. Aprovou as sete abaixo. A fase é de desenho: o extrator e o dado guardado não mudam.

**Status: concluída em 2026-10-17.** Prints em `docs/telas/fase-23/`.

- [x] **Passos numerados:** 1, 2, 3 na linha principal; 2.1, 2.2 dentro de galhos, blocos e laços. A ordem de leitura fica explícita mesmo quando o galho vai longe.
- [x] **Resumo do fluxo:** no topo da visão geral, uma frase com a linha principal e, abaixo, a lista numerada dos passos (a mesma numeração do grafo), sem IA. Clicar num item leva ao passo.
- [x] **O que o passo faz no mundo:** etiqueta "banco", "mensagem", "cache", "arquivo" ou "fila" no passo, inferida pelo tipo e pelo nome (Repository, HttpClient, cache e afins), com a explicação na legenda.
- [x] **Profundidade:** os passos do método de entrada ficam fortes; quanto mais fundo na cadeia de chamadas, mais leve. Botão "Resumo / Completo": o resumo mostra só o código do método de entrada, com os métodos chamados fechados.
- [x] **Caminho normal em destaque:** a linha principal fica mais grossa; a chegada a um `throw` fica vermelha e tracejada, como desvio.
- [x] **Nome legível:** `GetPendingAlertsAsync` aparece como "Get pending alerts", com a classe e o arquivo embaixo; o nome do código fica no texto de ajuda.
- [x] **Laço como moldura:** o que se repete fica num fundo próprio, com "Repete para cada ..." no cabeçalho.
  Feito: `flowDrawing` passou a devolver o número, a profundidade e o resumo em árvore de cada passo; `core/graph/reading.ts` (nome legível, etiqueta e modo "Resumo") e `core/graph/summary.ts` (a frase). A etiqueta é inferida pelo nome: "FormatTelegramMessage" não ganha "mensagem", só a classe que envia.
- [x] **Ajuste pedido pelo dono:** o bloco aparece como `try`, e as seções como `catch (Exception)` e `finally`, com o nome do código; "Tenta" e "Se der erro" deixavam o bloco vago.
- [x] **Defeito achado no caminho:** o fundo do `try` cobria a moldura do laço que fica dentro dele (o bloco de fora entrava na lista depois). Agora o maior fica atrás.

## Fase 24: código aberto no GitHub `[x]` (pedido do dono em 2026-10-26)

O dono vai abrir o Rastro ao público e colocá-lo no portfólio, no mesmo molde do Mirante: repositório próprio e público no GitHub (`felipemarcianodev`), `main` protegida que só aceita pull request, workflow de PR, release pelo Azure Pipelines e um card no `meu-portfolio`. Como é um pacote (`dotnet tool`), o card não tem site no ar: leva ao GitHub e ao NuGet.

**Decisões do dono (2026-10-26):**
- **Código aberto, licença MIT, grátis.** Saem a licença paga, a ofuscação e o emissor de chave. A ADR-002 é revista e a venda (Fase 15) deixa de existir como estava.
- **Histórico novo:** o GitHub começa com um commit inicial, já sem nada privado. Os commits de hoje ficam no Azure DevOps, privados, só para consulta.
- **Prints só de repositórios públicos:** os prints atuais mostram código de repositórios privados e não vão para o GitHub. No histórico deste plano, esses repositórios aparecem como repositório A, B e C, e as classes deles, com nomes genéricos.

**O que a varredura de 2026-10-26 achou:** nenhum segredo no histórico (chave, `.env` ou mapa da ofuscação), mas cerca de 60 menções a nomes de repositórios privados e a caminhos da máquina, em testes, prints, documentos, skills e no `CLAUDE.md`.

**Ordem e aprovações:** os itens 1 a 5 são trabalho neste repositório, com os gates de sempre. Criar o repositório público (item 6) e publicar no NuGet (item 7) mostram o código e o pacote ao mundo: cada um espera a confirmação do dono na hora.

- [x] **1. ADR-004, código aberto:** licença MIT; revoga a licença por chave e a ofuscação da ADR-002 (o `dotnet tool`, o SQLite e o resto continuam); a Fase 15 vira página do projeto, sem venda; a Fase 16 (enterprise) é revista. `visao.md` e `README` atualizados.
- [x] **2. Sem licença paga no código:** saem `Rastro.Licensing`, `Rastro.Licensing.Issuer`, o comando `rastro licenca`, a checagem no `Program.cs`, o Obfuscar e o mapa no `empacotar.ps1`, e o `docs/pacote/TERMOS.txt` (no lugar, o `LICENSE` MIT). Testes da licença removidos; o resto continua verde. A chave privada em `~/.rastro-emissor/` deixa de ser usada (o dono decide se apaga).
- [x] **3. Nada privado no código e nos documentos:** testes, exemplos e documentos passam a usar nomes genéricos; skills e `CLAUDE.md` sem caminhos da máquina. A lista de termos proibidos fica fora do repositório (citar o nome de um cliente num arquivo público já o expõe), e um script local confere a árvore inteira antes da exportação.
  Feito: `scripts/verificar-publico.mjs` lê a lista em `~/.rastro-publico/termos.txt` (ou `RASTRO_TERMOS`) e confere todo arquivo de texto rastreado, inclusive caminho com barra escapada. Testes e exemplos usam uma loja fictícia (pedidos, reservas); no histórico deste plano, os repositórios privados viraram repositório A, B e C.
- [x] **4. Documentação pública:** `README` da raiz no molde do Mirante (o que é, como instalar e usar, IA opcional, MCP, arquitetura), `SECURITY.md` (como relatar falha) e o plano e as ADRs revisados para quem chega de fora.
- [x] **5. Prints de repositórios públicos:** levantar fluxos do `saga-poc-dotnet` e da API do Mirante (os dois já são públicos) e refazer o conjunto essencial (levantamentos, levantamento novo, o grafo nas três visões, o resumo, a validação e as configurações). O `docs/telas/` público leva só esses prints, com o índice recomeçado.
  Feito: levantamentos do `ConsultasComCache.ObterAsync` (Mirante) e do saga-poc-dotnet, num Rastro de teste com `RASTRO_HOME` próprio. As Configurações entram só pelo recorte do formulário, porque a lista mostra a pasta real da máquina. Achado no caminho: num `try` com vários `catch`, o número do primeiro passo cobria o cabeçalho da seção (corrigido).
- [x] **6. Repositório no GitHub (com aprovação do dono na hora):** `felipemarcianodev/rastro-ai`, público, com descrição e tópicos; commit inicial único a partir da árvore limpa; `main` protegida (só PR, merge squash); Dependabot (NuGet, npm, Actions); workflow de PR com build e testes do backend e do frontend, actions fixadas por SHA. O GitHub vira o `origin`; o repositório do Azure DevOps fica arquivado. O `CLAUDE.md` passa a pedir branch e PR em vez de push na `main`.
  Feito em 2026-10-26, com a aprovação do dono: <https://github.com/felipemarcianodev/rastro-ai>, commit inicial assinado com o e-mail `noreply` do GitHub (como no Mirante), `main` protegida também para o administrador (PR, checks `backend` e `frontend`, histórico linear, merge squash) e relato privado de falhas ligado. No repositório local, o Azure DevOps virou o remoto `azure`, e o histórico antigo ficou na branch `main-azure`. Falta o dono arquivar o repositório do Azure DevOps.
- [x] **7. Publicação no NuGet (com aprovação do dono na hora):** workflow de release no GitHub Actions que testa, empacota e publica o `rastro-ai` pela Publicação Confiável do nuget.org, sem chave guardada. Depende da conta no nuget.org (pendência da Fase 14).
  Mudança de 2026-10-30: o plano previa o Azure Pipelines, como o Mirante, mas o código agora mora no GitHub, e o nuget.org desencoraja chave de API em publicação automatizada e recomenda a Publicação Confiável, que hoje só funciona com o GitHub Actions. O `.github/workflows/release.yml` dispara com a tag `v<versão>` (igual ao `VERSION`), roda os testes do backend e do front (com npm audit), gera o pacote pelo `empacotar.ps1` e troca o token OIDC do GitHub por uma chave temporária (`NuGet/login`) para o `dotnet nuget push`. O nome `rastro-ai` estava livre no nuget.org, e o pacote 0.1.0 sai sem ofuscação, com licença MIT, README e o commit do GitHub. Publicado em 2026-10-30, com a aprovação do dono: tag `v0.1.0`, política de Publicação Confiável no perfil `felipe.marciano` (só o pacote `rastro-ai`) e secret `NUGET_USER` no GitHub. A primeira execução falhou no login porque a política ainda não existia; criada a política, o job foi reexecutado e publicou o pacote: <https://www.nuget.org/packages/rastro-ai>.
  Pedido do dono no mesmo dia: o job de publicação passou a usar o environment `release` do GitHub, que só aceita tag `v*` e espera a aprovação do dono; a política do nuget.org exige esse environment. Assim, nem uma tag criada por engano publica sem um clique.
- [x] **8. Card no portfólio:** no `meu-portfolio` (`projects.ts`), com problema, solução e resultado, tecnologias (.NET 10, Roslyn, MSBuild, Angular 20, Foblex Flow, SQLite, MCP), imagem do grafo, `githubUrl` e o link do NuGet no texto, sem `demoUrl`. Segue as regras de commit e release daquele repositório.
  Feito: o card entrou em 2026-10-26 só com o GitHub. Em 2026-10-30, com o pacote no ar, o modelo de projeto do portfólio ganhou o `packageUrl` opcional, e o card, um botão NuGet ao lado do GitHub (o texto longo não aparece no card, então só o botão leva ao pacote). Saiu na release v1.26.0 do portfólio, que só faz o deploy do site.

## Fase 22: modelos de ramificação `[ ]` (ideia do dono em 2026-10-17, a detalhar)

Próximo passo da Fase 21: o usuário escolhe como quer ver a ramificação, entre modelos prontos. Os modelos ainda serão definidos com o dono.

## Fase 15: página do projeto e identidade visual `[ ]` (revista pela ADR-004: o Rastro é aberto e gratuito, sem venda)

- [ ] Identidade visual do produto (cor e tema), escolhida pelo dono a partir de propostas aplicadas na tela.
- [ ] Landing page em português, inglês e espanhol.
- [-] ~~Preço único e baixo no começo; pagamento por serviço pronto (Lemon Squeezy, Paddle ou similar) que emite a licença sozinho e manda por e-mail.~~ Cancelado pela ADR-004.
- [ ] Textos do produto (tela, pacote, mensagens da CLI) nos três idiomas.
  Referência (dono, 2026-11-07): um projeto irmão já tem multi-idioma no frontend e no backend; ao detalhar esta fase, partir do caminho de lá. Vale fazer depois que os rótulos da tela estabilizarem (o plano de UX de 2026-11-07 ainda mudou vários).

## Fase 16: IA pelo endpoint da empresa `[ ]` (futuro)

O Rastro é gratuito e de código aberto (ADR-004); o antigo "plano enterprise" deixou de ser um plano pago. Fica só a funcionalidade, para quem trabalha numa empresa que só libera o endpoint de IA contratado por ela.

- [ ] IA com os tokens contratados pela empresa: endpoint próprio (Azure OpenAI, Bedrock ou gateway interno) no modo provedor.
- [-] ~~Licença por assentos e emissão em lote.~~ Cancelado pela ADR-004.

## Fase 25: fluxo único entre sistemas (C# para C#) `[x]` (pedido do dono em 2026-10-30)

**Status: concluída em 2026-10-30.** Prints em `docs/telas/fase-25/` (APIs fictícias da loja, as mesmas das fixtures).

Hoje cada sistema vira um levantamento separado: o `Survey` tem um repositório, uma solution e um commit; a chamada HTTP para outra API é uma folha (`External`); e a regra cruzada (`CrossFlow`) só olha o mesmo repositório. O dono quer um levantamento só, com um grafo só, que siga a chamada HTTP para dentro da outra API e mostre as bifurcações de cada sistema no mesmo desenho, com a numeração corrida (1, 2, 2.1...) sem recomeçar ao trocar de sistema.

A validação usa uma fixture com sistemas fictícios, no padrão da loja de teste: `api-pedidos` (que cancela o pedido e pede o estorno) e `api-pagamentos` (que estorna por cartão ou boleto).

**Decisões do dono (2026-10-30):**
- **Edição, prompt e validação continuam num trecho só.** As Fases 7 a 9 assumem um repositório (rascunho, prompt, build e testes). Num levantamento com vários trechos, a tela só deixa editar e validar dentro de um trecho; editar através de sistemas fica para uma fase futura.
- **Esta fase vem antes da Fase 26:** o fluxo entre APIs já entrega as bifurcações de regra, que ficam no backend, e o modelo de trechos precisa estar provado antes de entrar a tela.

**Modelo:**
- [x] `Survey` passa a ter **trechos** (`SurveySegment`): repositório, solution, ponto de entrada, commit e branch. O primeiro trecho é a entrada do fluxo; os outros nascem da costura.
- [x] **Id e posição com o trecho:** o id do nó (`m:<símbolo>`, `x:<símbolo>`) e a `SourceLocation` passam a levar o trecho. Dois repositórios com o mesmo namespace (biblioteca compartilhada, cópia de projeto) não podem dar o mesmo id nem o mesmo `arquivo:linha`.
  Feito: os nós de outro sistema têm o id prefixado (`<repositório>::<id>`, `SystemIds`) e o campo `System`; a `Location` continua relativa à raiz daquele repositório, e o `GET source` recebe `system`. O primeiro sistema mantém os ids de antes, então anotações e levantamentos antigos continuam valendo.
- [x] `FlowNode` ganha o sistema (nome do repositório cadastrado); `Project` continua sendo o assembly.
- [x] **Ligação HTTP na aresta, não no nó:** o `External` é um nó só para todas as chamadas ao mesmo método (`HttpClient.PostAsync`), por isso o verbo, a rota e o destino ficam na `FlowEdge`, junto da `Location` da chamada que já existe desde a Fase 13. Duas chamadas para APIs diferentes nunca se misturam.
- [x] `RastroSchema.Version` sobe; o levantamento antigo é lido como um trecho só.
- [x] `/stale` confere o commit de **cada** trecho e diz qual sistema mudou.

**Índice de rotas:**
- [x] Por repositório cadastrado, o extrator monta o índice `verbo + template de rota → método` a partir de `[Route]` e `[Http*]` dos controllers e dos `Map*` das minimal APIs.
- [x] O índice é guardado por commit, para a costura não precisar da solution do outro sistema aberta só para achar a rota.

**Costura:**
- [x] Na chamada HTTP (`HttpClient.*Async` e as extensões de `System.Net.Http.Json`, também dentro de client tipado), o extrator resolve o verbo e a rota: literal, constante, interpolação e concatenação.
  Mudança no caminho: em vez de ler a `BaseAddress` no `appsettings`, a parte variável no começo da URL é tratada como o endereço base e sai, e a rota casa pelo fim do template quando não casa inteira (`MatchesSuffix`, com pelo menos dois segmentos). Dá o mesmo resultado sem ler configuração por ambiente.
- [x] Refit (interface com atributos `[Get]`, `[Post]`): feito na Fase 29 (issue #31).
- [x] Rota resolvida e com par no índice de outro repositório: o percurso continua dentro dele, como um trecho novo, e o `MaxDepth` vale para o total.
- [x] Rota não resolvida vira `Unresolved` com o motivo novo `RouteNotResolved = 7`; rota resolvida sem ninguém que a atenda vira `NoMatchingEndpoint = 8`. Fica para a IA propor e o usuário confirmar, como hoje.
- [x] Rota que casa com mais de um destino (duas versões da mesma API cadastradas) também vira `Unresolved`, nunca uma escolha silenciosa.
- [x] Prova da ligação HTTP no `ProofValidator`: o método de destino existe no trecho de destino e o template da rota casa com o verbo e a rota chamados. O validador deixa de receber uma `Solution` só.
- [x] Ciclo entre sistemas (A chama B, que chama A) é cortado no primeiro retorno, como a recursão.

**Memória e tempo (o maior risco da fase):**
- [x] Hoje cada análise abre um `MSBuildWorkspace` (`SolutionLoader`). A costura abre **uma solution por vez**: percorre o trecho, guarda o grafo parcial, fecha o workspace e abre o próximo. Nada de três solutions carregadas ao mesmo tempo.
- [x] Medição registrada neste plano: pico de memória e tempo de um levantamento com dois e com três trechos, comparados ao de um trecho.
  Medido em 2026-10-30 com três repositórios .NET reais (cópias das APIs fictícias, cada um com a sua solution e o seu git), API nova a cada cenário, memória da API somada à dos processos filhos (o MSBuild carrega a solution num processo à parte):

  | Levantamento | Trechos | Nós | Pico de memória | Tempo |
  |---|---|---|---|---|
  | `PedidosController.Cancelar`, sem costura | 1 | 7 | 240 MB | 4,3 s |
  | Seguindo para `api-pagamentos` | 2 | 14 | 301 MB | 5,9 s |
  | Seguindo para `api-pagamentos` e `api-notificacoes` | 3 | 16 | 319 MB | 7,4 s |

  Cada trecho a mais custa cerca de 1,5 s (a carga da solution) e pouca memória, porque a solution anterior é fechada antes de abrir a próxima.

**API, MCP e tela:**
- [x] `POST api/surveys` aceita a lista de repositórios em que a costura pode entrar (só os cadastrados; a regra de pasta permitida continua valendo). Lista vazia significa sem costura.
- [x] MCP: `propose_edge` aceita a ligação HTTP com a prova nova, e uma ferramenta lista as ligações entre sistemas do levantamento.
- [x] A linha do tempo agrupa os passos por sistema (faixa com o nome e a linguagem), e a ligação HTTP tem desenho próprio, com verbo e rota.
- [x] Recolher e abrir um trecho inteiro, para o grafo com vários sistemas continuar legível.
- [x] O painel do passo mostra o sistema e o commit do trecho.
- [x] Edição e validação travadas no trecho (decisão do dono): a tela diz em qual trecho o rascunho vale e não oferece editar passo de outro sistema.
- [x] A regra cruzada (`CrossFlow`) passa a olhar os levantamentos que têm algum trecho em comum, e não só o mesmo repositório.
- [x] Export Mermaid com um `subgraph` por sistema; o C4 usa os trechos como os sistemas do C1.
- [x] A IA (modo provedor) completa a chamada HTTP sem destino com o repositório de destino (`system`), lendo as rotas dos repositórios da costura; regra e sugestão leem o código do repositório do passo.
- [x] Documentação para o cliente: `README.md`, `docs/pacote/README.md` e `visao.md`, pela regra nova do `CLAUDE.md` ("Mudança no ambiente do cliente").
- [x] **Defeitos achados no uso:** a moldura do sistema cobria só a primeira linha do trecho e deixava os galhos de fora; e, na visão consolidada, a entrada de outro sistema sumia como método de passagem, e a seta "VERBO rota" caía no meio da outra API. Os dois foram corrigidos.

**Aceite:**
- [x] Fixture `api-pedidos` + `api-pagamentos`: um levantamento a partir de `PedidosController.Cancelar` chega a `EstornosController.Criar` e mostra as duas bifurcações (prazo e forma de pagamento), com a numeração corrida.
- [x] Rota montada em tempo de execução vira `Unresolved` com o motivo, e uma proposta da IA com a prova certa fecha a ligação como inferida.
- [x] Mudar um commit só no `api-pagamentos` marca o levantamento como desatualizado, apontando esse trecho.
- [x] Medição de memória e tempo registrada, com uma solution aberta por vez.
- [x] Os testes de arquitetura continuam garantindo que `Rastro.Core` e `Rastro.Extractor` não referenciam IA.

## Fase 26: extrator Angular `[x]` (pedido do dono em 2026-10-30; depende da Fase 25)

**Status: concluída em 2026-10-30.** Prints em `docs/telas/fase-26/` (a loja fictícia em Angular e as duas APIs fictícias).

Faz o primeiro passo do fluxo ser o clique na tela. Contraria duas decisões registradas: "Só .NET no início" (`visao.md`) e a leitura do frontend em "Fora de escopo" (abaixo). Por isso começa por uma ADR.

- [x] **ADR-005, segunda linguagem no extrator:** revoga as duas decisões acima. Opção recomendada: um processo Node com o compilador do TypeScript, chamado pelo `ProcessRunner` (sem shell, com timeout), devolvendo o mesmo `FlowGraph`. Alternativa recusada: parser próprio em C#, que não resolve tipos nem injeção. A ADR decide como o `typescript` chega à máquina sem pesar o `dotnet tool`.
- [x] Extrator Angular opcional: ligado só quando o Node está no PATH. Sem Node, a tela não mostra a opção (nada de botão morto).
- [x] Ponto de entrada do front: `Componente.metodo` (exemplo: `DetalhePedidoComponent.cancelar`), ou o `(click)` do template que aponta para ele.
- [x] Percurso: método, service injetado e `HttpClient.get/post/put/delete`, com `if`, `switch`, `@if` e `*ngIf` do template como bifurcação, e `subscribe` e `pipe` como sequência.
- [x] Injeção resolvida pelo construtor e pelo `inject()`. Provider trocado em tempo de execução vira `Unresolved`.
- [x] A URL vem de `environment.*` + literal, e a costura da Fase 25 liga a chamada ao controller do backend.
- [x] **Aceite:** a fixture `loja-web` + `api-pedidos` + `api-pagamentos` dá, num levantamento só, o fluxo do clique em "Cancelar" até o gateway de pagamento, com as bifurcações dos três sistemas.
- [x] Feito: o script `extractor-ts/extract.mjs` (com `lib/`), testado com `node --test` (49 testes, fixture `loja-web`); no backend, `NodeRuntime`, `TypeScriptExtractor` (pelo `ProcessRunner`, com timeout) e o `CrossSystemAnalyzer` com o primeiro trecho Angular. O `typescript` vem do repositório analisado (ADR-005), e o pacote leva só o script. O catálogo aceita `tsconfig.json` e `tsconfig.*.json` e lê `.ts` e `.html`; sem Node, a API responde `ANGULAR_UNAVAILABLE` e o `/api/health` diz `angular: false`.
- [x] Tela: a moldura e o painel dizem a linguagem do trecho ("Angular" ou ".NET"); com o primeiro sistema Angular e outros depois, ele também ganha moldura. O formulário troca o exemplo do ponto de entrada quando o caminho é um `tsconfig` e avisa quando falta o Node.
- [x] CI: job `extrator-ts` no workflow de PR, e o `npm ci` do `extractor-ts` antes dos testes do backend no PR e na release (o teste de integração roda o Node de verdade).
- [x] Documentação para o cliente: `README.md`, `docs/pacote/README.md` e `visao.md` (a decisão "Só .NET no início" revista pela ADR-005).
- [x] Guardas onde o Angular ainda não entra: validação da mudança, prova de proposta que não é HTTP e as ferramentas do MCP que abrem a solution respondem que o Angular não é lido ali, em vez de tentar abrir um `tsconfig` no MSBuild.
- [ ] Leitor de template: `@else` não nega a condição do `@if`, e `@switch`, `ng-template` e diretivas próprias ficam fora; só vale o primeiro elemento que dispara o método.
- [ ] Interface com várias implementações no Angular não consulta os providers: vira não resolvido com os candidatos.

## Fase 27: visão consolidada em Mermaid, editável `[x]` (pedido do dono em 2026-10-30; não depende da Fase 25)

**Status: concluída em 2026-10-30.** Prints em `docs/telas/fase-27/` (levantamento do Mirante, repositório público).

O grafo da tela mostra cada método, bloco e chamada, e o export Mermaid da Fase 12 (`core/graph/export.ts`) só copia esse mesmo grafo em texto. O dono quer outra coisa: uma visão consolidada, de leitura de negócio, com poucos passos em frase ("Usuário abre o detalhe do pedido", "Pedido já enviado?", "API cria o estorno"), renderizada dentro do Rastro, com o texto do Mermaid editável e, principalmente, as cores.

O exemplo que o dono mandou vem de um sistema privado e não entra no repositório: testes, documentação e prints usam a loja fictícia (ADR-004). Só o código que monta a visão é público.

**Geração (determinística, sem IA):**
- [x] O grafo vira um `flowchart` consolidado: ficam a entrada, as bifurcações (com o texto de cada ramo), as chamadas externas e HTTP, os `throw` e os retornos antecipados, e os métodos que têm regra escrita. Métodos de passagem (que só repassam a chamada) somem e a ligação atravessa.
- [x] O texto do passo vem da regra do nó (`NodeRule`) quando existe e, senão, do nome legível da Fase 23 (`core/graph/reading.ts`).
- [x] **Id estável no Mermaid:** derivado do id do nó, não da posição na lista (o export atual usa `n0`, `n1`). Assim a cor e o texto editados continuam no passo certo quando o levantamento é refeito.
- [ ] Com a Fase 25, um `subgraph` por sistema; antes dela, o fluxo inteiro num bloco só. (Entra na Fase 25.)

**Renderização:**
- [x] A biblioteca `mermaid` entra como dependência do front, empacotada no build (nada de CDN: o diagrama nunca sai da máquina) e carregada só quando a visão é aberta, para não pesar nas outras telas.
- [x] Segurança: `securityLevel: 'strict'`, sem `click` nem HTML no rótulo. O texto vem do código analisado e da edição do usuário, então o SVG passa pela sanitização antes de entrar na página.
- [x] Erro de sintaxe na edição mostra a mensagem e a linha, e a prévia mantém o último desenho válido.

**Edição:**
- [x] Editor do texto ao lado da prévia, que se atualiza enquanto o usuário digita (com atraso curto).
- [x] **Cores:** clicar num passo da prévia abre a escolha de cor (fundo, borda e texto). A escolha vira `classDef` e `class` no próprio texto do Mermaid, que continua sendo a única fonte. As sugestões saem dos tokens das paletas do tema; o seletor livre aceita qualquer cor.
- [x] Estilo por tipo de passo (todas as bifurcações, todas as chamadas externas) num clique, além do estilo de um passo só.
- [x] Desfazer e refazer na edição.
- [x] A versão editada fica salva no levantamento, com o commit de onde foi gerada. "Gerar de novo" pede confirmação pelo diálogo do tema e mantém as cores e os textos dos passos que ainda existem (pelo id estável); passo que sumiu do código sai, e a tela diz quantos.

**Exportação:**
- [x] Copiar o Mermaid, baixar `.mmd`, SVG e PNG da visão consolidada, com as cores editadas.

**Aceite:**
- [ ] Na fixture da loja (as APIs fictícias nascem na Fase 25), a visão consolidada do cancelamento do pedido sai com as bifurcações e as chamadas externas, sem os métodos de passagem, e o mesmo grafo dá sempre o mesmo texto.
- [x] Mudar a cor de um passo e de um tipo de passo, salvar, refazer o levantamento e gerar de novo: as cores continuam.
- [x] Texto com `<script>` ou `click` num rótulo não executa nada (teste do front).
- [x] A visão só carrega a biblioteca `mermaid` quando aberta (conferido no build pelos chunks).
- [x] Feito: `core/graph/consolidated.ts` (geração, id por hash FNV-1a do id do nó, estilo por passo e por tipo, refazer sem perder a edição), `core/mermaid-render.ts` (Mermaid 11 em modo `strict`, carregado sob demanda) e a página `pages/diagram/` na rota `surveys/:id/visao`, com o link "Visão consolidada" na barra do fluxo. No backend, a tabela `survey_diagrams` (migration `VisaoConsolidada`) e as rotas `GET`, `PUT` e `DELETE api/surveys/{id}/diagram`.
- [x] **Edição que acompanha o levantamento novo:** refazer o levantamento cria outro levantamento, então o `GET` do diagrama, sem diagrama próprio, devolve o salvo mais recente de um levantamento anterior do mesmo repositório e ponto de entrada (`fromSurveyId`). A tela gera o texto do grafo antigo para saber o que o dono editou de fato, junta com o gerado do grafo atual e avisa quantos passos editados saíram.
- [x] **Defeito achado no uso:** um `return` dentro de um método de passagem encerrava o fluxo inteiro, e o resto do desenho ficava solto. Agora ele encerra só aquele método e segue para o passo seguinte de quem chamou.
- [x] A barra e o estado de página (voltar, título, dados, ações) subiram para o tema (`theme/_page.scss`), usados pelo fluxo e pela visão consolidada.
- [x] Levantamento de UX da tela nova ([`plano-ux-2026-10-30-visao-consolidada.md`](../frontend/plano-ux-2026-10-30-visao-consolidada.md)): os achados de risco baixo e médio entraram na fase (escolha do passo pelo teclado, painel de cor flutuante, zoom da prévia, erro que não some, um botão primário só).
- [ ] **Alto risco, espera aprovação do dono (D15):** trocar o texto de ajuda do link "Visão consolidada" ("O fluxo todo num diagrama Mermaid editável") por algo sem jargão, como "Um desenho único do fluxo, que você pode editar e exportar", e deixar "Mermaid" só nos botões de exportação.

## Fase 28: a visão consolidada entra no fluxo `[x]` (pedido do dono em 2026-10-30, no uso da 0.2.0)

**Status: concluída em 2026-10-30.** Prints em `docs/telas/fase-28/` (loja fictícia).

O dono usou a visão consolidada da Fase 27 e achou três problemas: o zoom é difícil (a prévia só rola, e o zoom muda a largura em passos de 25%); clicar num passo só abre a cor, sem o contexto que as outras visões dão no painel do passo; e ele esperava a visão como um desenho a mais do fluxo, como já são o horizontal, a linha do tempo e a árvore, e não como uma tela à parte.

**Desenho a mais do fluxo:**
- [x] O botão que troca o desenho passa por quatro: horizontal, linha do tempo, árvore e consolidado (Mermaid), e volta ao horizontal. A escolha fica guardada no navegador, como as outras.
- [x] No desenho consolidado, clicar num passo escolhe o mesmo passo do grafo e abre o painel do passo (código, regra, ligações, anotação), como nas outras visões. O passo escolhido no painel (ou pela busca) acende no desenho e, se estiver fora da vista, o desenho vai até ele.
- [x] O desenho usa a edição salva da visão (cores e textos do dono) e, sem edição, o gerado do fluxo. Um botão leva à tela de edição da Fase 27, que continua sendo onde se mudam o texto e as cores.
- [x] Na edição do fluxo (Fase 7) e na comparação de levantamentos (Fase 12) o desenho é o do grafo, que mostra o rascunho e as diferenças; o consolidado volta quando elas acabam.
- [x] O link "Visão consolidada" da barra passa a trocar o desenho do fluxo, no lugar de abrir a outra tela.

**Zoom e movimento, nas duas telas:**
- [x] Um palco comum para o SVG do Mermaid: arrastar o fundo move o desenho; a roda do mouse (e a pinça do touchpad) aproxima e afasta em volta do cursor; botões de aproximar, afastar, enquadrar e 100%, como os do grafo. O desenho abre enquadrado e não perde o zoom quando o texto muda.

**Contexto na tela de edição:**
- [x] Com um passo escolhido, o painel de cor ganha "Ver no fluxo", que abre o fluxo com aquele passo escolhido e o painel do passo aberto (parâmetro `passo` na rota do fluxo).

**Aceite:**
- [x] Testes do palco (arrastar move, roda dá zoom em volta do cursor, enquadrar cabe na vista), do desenho consolidado no fluxo (clique escolhe o nó certo do grafo, inclusive passo repetido; seleção de fora acende o passo) e da troca de desenho com quatro estados.
- [x] Verificação no navegador com clique de verdade, num repositório público.
- [x] Feito: `pages/diagram/diagram-stage.component.ts` (o palco, nas duas telas), `pages/survey/consolidated-view.component.ts` (o desenho no fluxo), `core/diagram-source.ts` (a edição salva, herdada ou o gerado, igual nas duas telas), `LayoutDirection.Consolidated` com a escolha guardada em `core/graph/layout.ts`, e `nodeOfStep` em `core/graph/consolidated.ts`. A coluna de botões do canto subiu para o tema (`.canvas-tools`), e o botão que liga e desliga ganhou o estado ligado no tema.
- [x] **Achados na verificação:** no consolidado, clicar num passo com o painel recolhido não mostrava contexto nenhum; agora o clique abre o painel. O "Ver somente este fluxo" vale também no consolidado: o desenho é o do recorte, com as cores do dono nos passos que ficam. Fluxo alto abria enquadrado inteiro em 25%, ilegível; agora abre na largura, a partir do topo, e o ⤢ continua enquadrando tudo.

## Fase 29: issues dos extratores `[x]` (abertas pelo dono em 2026-10-30)

- [x] **#30, monorepo Angular:** o TypeScript e o nome do projeto são procurados da pasta do `tsconfig` escolhido até a raiz do repositório, sem sair dela. Antes, o extrator só olhava a raiz, e o app numa subpasta (`frontend/`) falhava com "rode o npm install". Teste EXT-TS-026; ADR-005, README e README do pacote atualizados.
- [x] **#31, Refit como chamada HTTP entre sistemas:** a chamada a um método de interface com `[Get]`, `[Post]`, `[Put]`, `[Patch]`, `[Delete]`, `[Head]` ou `[Options]` do namespace `Refit` vira um nó `h:` com verbo e template do atributo, como o `HttpClient` (`{id}` e `{**caminho}` viram o buraco `{}`; query e endereço base saem). O atributo é reconhecido pelo símbolo (namespace `Refit`); sem a dll resolvida, vale o atributo sem símbolo quando o arquivo tem `using Refit` ou o nome qualificado. Sem as chamadas HTTP ligadas, vira nó externo, nunca "sem implementação". Testes: `EXT-REFIT-001` a `EXT-REFIT-006`, `INF-REFIT-001` a `INF-REFIT-003`.
- [x] **#32, seguir a abertura de modal:** `MatDialog.open(Componente)` e `Dialog.open(Componente)` do CDK (identificados pelo tipo resolvido, não pelo nome da variável) abrem o galho "abre o modal Componente", com os métodos que o template do modal (inline ou `templateUrl`) liga a eventos como passos "ação do usuário no modal" (arestas inferidas), e o `afterClosed()`/`closed` segue como continuação. Respeita `--depth` e não entra em laço se o modal abre a si mesmo. Testes: EXT-TS-090 a EXT-TS-097 (os IDs 030 a 033 já eram do percurso). Fica fora: serviço próprio que repassa para o `MatDialog`.

## Correção da etiqueta "banco" `[x]` (pedido do dono em 2026-11-04)

- [x] A regra marcava como banco qualquer nome com "Repository" no meio, e `RepositoryCatalog` e `RepositoryConfigFile`, que tratam de pastas e de configuração, ganhavam a etiqueta. Agora "Repository" só vale no fim do nome da classe (`OrderRepository`, `IRepository<T>`, `OrderRepositoryBase`). A etiqueta continua sendo uma leitura pelo nome: uma classe `GitRepository` ainda ganha "banco".

## Nomes em repositório público `[x]` (pedido do dono em 2026-11-04)

- [x] Hook `nomes-publicos.mjs`: `git commit` e `gh pr create` neste repositório são recusados se a mensagem, o título, o corpo ou as linhas adicionadas citarem uma pasta vizinha ou um termo da lista privada, se houver `Co-authored-by` ou se o e-mail do git não for o noreply do GitHub. Autorizado pelo dono, o nome entra numa lista fora do repositório. Testes `HOOK-NOMES-001` a `011`.
- [ ] O hook só protege sessões do Claude Code: um commit feito à mão ou em outra máquina sem o hook passa. Falta o mesmo cuidado no CI, com a lista de nomes em secret.
- [ ] Reescrever o histórico da `main` para tirar as linhas `Co-authored-by` já publicadas: autorizado pelo dono, à espera da execução, porque o controle de permissões recusou o comando.
- [ ] Demais achados do levantamento de governança de 2026-11-04: verificador público (raiz e lista vazia), título do PR no CI, testes de governança no CI, `deny` de comandos irreversíveis e regras contraditórias.

## Mapa do fluxo, regras e perguntas (Fases 30 a 34) (pedido do dono em 2026-11-04)

O dono usou o Rastro num fluxo real de ponta a ponta (tela Angular, duas APIs .NET e um CRM fora do código) e sentiu falta de um nível acima dos desenhos que existem. A linha do tempo, a árvore e o consolidado mostram o fluxo passo a passo; falta a visão de quem chega agora, no nível de um diagrama C2: por quais sistemas a funcionalidade passa, o que cada um faz em poucos blocos, o que decide o caminho e o que volta para o usuário. A partir dali, clicar num bloco leva ao detalhe só daquele trecho.

Na mesma conversa, o dono pediu que o Rastro responda às perguntas que um TL, um Staff ou um gerente fariam ("dá para cancelar depois do status X?", "o que fica pela metade se a outra API cair?"), primeiro com o que o código prova e, com IA ligada, em pergunta livre.

O protótipo aprovado usa dados fictícios e fica fora do repositório. Testes, documentação e prints usam a loja fictícia (ADR-004).

**Decisões do dono (2026-11-04):**
- **O mapa é mais um desenho do fluxo**, como decidido na Fase 28 para o consolidado: entra no botão que troca o desenho, não numa tela à parte.
- **Bloco de passo:** os métodos chamados direto pela entrada de cada trecho (profundidade 1), com o que eles chamam no mesmo sistema recolhido dentro do bloco.
- **Chamada repetida:** a mesma rota chamada de dois lugares vira duas pontes, uma para cada lugar.
- **Decisão no mapa:** só a que muda o desfecho em pelo menos um dos ramos (retorna, lança exceção ou chama outro sistema). Uma bifurcação que só registra log fica dentro do bloco.
- **Núcleo sem IA (ADR-001):** mapa e regras são determinísticos, no `Rastro.Core`. A IA só entra nas perguntas (Fase 33) e sempre com fonte.

## Fase 30: mapa do fluxo, núcleo e API `[x]`

**Status: concluída em 2026-11-04.** Só backend; a tela entra na Fase 31.

**Modelo (`Rastro.Core/Graph/FlowMap.cs`):**
- [x] `FlowMap`: faixas, blocos, ligações e caminhos, mais o aviso de corte quando o limite de caminhos é atingido (`PathsTruncated`).
- [x] `MapLane`: um sistema do levantamento (o repositório do primeiro trecho e os da costura) e a faixa "fora do código", que junta os nós externos e as chamadas HTTP sem destino resolvido. Ids fixos `:principal` e `:fora`, que não colidem com nome de repositório.
- [x] `MapBlock`: tipo (`MapBlockKind`: entrada, passo, decisão, fora do código), faixa, ordem, título (o rótulo do nó, ou a condição na decisão; o front aplica o nome legível da Fase 23), o nó que dá nome ao bloco, símbolo, local no código, os ids dos nós que o bloco recolhe e, na decisão, a condição e os desfechos.
- [x] `MapOutcome`: o texto do ramo (then, else, case), o primeiro bloco do ramo, se o ramo encerra o fluxo (`return` ou `throw`) e se encerra com falha (`throw`). O `if` sem `else` ganha o desfecho "else" vazio, para o caminho em que a condição é falsa existir.
- [x] `MapLink` no lugar da `MapBridge` planejada: uma lista só de ligações, com o tipo (`MapLinkKind`: sequência, desfecho, chamada), o texto do ramo e o `HttpCall` quando a chamada é HTTP. A tela desenha todas as setas a partir dela.
- [x] `MapPath`: blocos na ordem de execução, as escolhas feitas em cada decisão (`MapChoice`) e se termina em falha; no máximo 8, os sem falha primeiro.

**Montagem (`FlowMapBuilder.Build(FlowGraph)`), determinística:**
- [x] Faixa por `FlowNode.System` (nulo é o primeiro trecho), na ordem em que o fluxo entra em cada sistema; fora do código por último.
- [x] Bloco de entrada: o `EntryNodeId` e cada destino resolvido de uma aresta com `Http`.
- [x] Bloco de passo: cada método chamado pelo trecho; o que ele alcança no mesmo sistema vira `NodeIds` do bloco, e as chamadas para outro sistema ou para fora saem dele como ligações.
- [x] **Método transparente (descoberto ao montar):** com profundidade 1 pura, o controller que só repassa para um serviço virava um bloco único com o fluxo inteiro dentro. O método que orquestra outros (chama método ou decide) fica transparente, e os passos dele sobem para o mapa, quando é o único método que o pai chama (passagem) ou quando passa de um terço dos nós do trecho (mínimo de 12 nós).
- [x] Decisão promovida pela regra do desfecho: um ramo tem `return` ou `throw` no próprio nível ou alcança outro sistema ou fora do código. Chamada de log não conta, porque o extrator já não faz nó externo de `Microsoft.Extensions.Logging`.
- [x] Fora do código: um bloco por nó externo (`x:`) ou chamada HTTP sem destino (`h:`), com uma ligação de chamada para cada lugar que o chama.
- [x] Caminhos por enumeração das escolhas (odômetro): o fim de outro sistema volta para quem chamou e não encerra o caminho.
- [x] O mesmo grafo dá sempre o mesmo mapa (arestas por `Order` e por id; aresta removida no rascunho não entra). Método que chama a si mesmo não entra em laço.

**API:**
- [x] `GET api/surveys/{id}/map`: o mapa do levantamento; levantamento inexistente ou sem grafo devolve 404, como as outras rotas do grafo.

**Aceite:**
- [x] Testes do construtor (CORE-MAP-001 a 012) com o grafo da loja montado à mão (`api-pedidos` e `api-pagamentos`, como na Fase 25): faixas na ordem, serviço transparente, decisão que lança exceção com desfecho de falha, `if` que não muda o desfecho fora do mapa, chamada do estorno com verbo e rota, externo e HTTP sem destino na faixa de fora, `switch` do meio de pagamento com um desfecho por `case`, os três caminhos com o de falha por último, corte em 8 caminhos, mesmo mapa para o mesmo grafo, entrada inexistente e método recursivo.
- [x] Testes da rota (API-MAP-001 e 002).
- [x] Teste de arquitetura verde: nada de IA no núcleo.
- [ ] Conferir o mapa num levantamento real grande (com os desenhos da Fase 31), principalmente o limite de um terço que deixa um método transparente.

## Fase 31: o mapa como desenho do fluxo `[x]` (depende da Fase 30)

**Status: concluída em 2026-11-04**, com os prints e o levantamento de UX pendentes (abaixo).

- [x] O botão que troca o desenho passa a ter cinco estados: horizontal, linha do tempo, árvore, consolidado e mapa (`LayoutDirection.Map = 4`). Na edição e na comparação, o botão pula o consolidado e o mapa, que mostram o grafo inteiro e não o rascunho.
- [x] Faixas por sistema em colunas, blocos posicionados pela ordem do fluxo (`core/graph/flow-map.ts`, `placeRows`: a linha do bloco é a maior linha de quem chega a ele, mais 1; o bloco chamado de outra faixa fica na linha de quem chama; dois blocos nunca dividem faixa e linha) e ligações em SVG, com a chamada tracejada e "VERBO rota" no rótulo. Até 640px, uma coluna só, com o nome da faixa no bloco.
- [x] Seletor de caminho ("N. desfechos escolhidos", com a marca de falha) e "Todos"; o caminho acende blocos e ligações com a cor da trilha e apaga o resto. Clicar num desfecho, na ligação ou no painel, escolhe o caminho dele. Aviso quando há mais caminhos que os mostrados.
- [x] Clicar num bloco escolhe o nó dele no grafo (o painel do passo abre como nas outras visões) e abre o painel do bloco: os passos que ele recolhe com arquivo e linha, os desfechos, as chamadas que saem dele e anterior e seguinte no caminho escolhido (botões e setas do teclado; Esc limpa a seleção pelo atalho da página).
- [x] "Ver só este trecho": o recorte que existe aceita um nó, então ele recebe o nó que dá nome ao bloco, e a página volta ao desenho horizontal para mostrar o recorte (o mapa é sempre do grafo inteiro).
- [x] Testes: `flow-map.spec.ts` (posição, caminhos, rótulos) e `flow-map-view.component.spec.ts` (caminho, desfecho, bloco, anterior e seguinte, recorte, aviso de corte, erro, vazio, carregando); troca de desenho com cinco estados.
- [ ] Recorte com todos os `NodeIds` do bloco: pede o recorte por conjunto de nós.
- [ ] Prints em `docs/telas/fase-31/` e levantamento de UX da tela nova (precisa do Rastro rodando com a loja fictícia).

## Fase 32: regras do fluxo `[x]` (depende da Fase 30)

**Status: concluída em 2026-11-04**, no mesmo commit da Fase 33 (as duas dividem a rota do controller e o painel do mapa).

- [x] `FlowRuleExtractor` (núcleo, sem IA): cada decisão do mapa vira uma regra com a condição, o efeito (`RuleEffect`: bloqueia, quando um ramo encerra com `return` ou `throw`; efeito colateral, quando um ramo chama outro sistema ou fora do código; permite, quando só escolhe o caminho), o texto gerado ("Se X, encerra o fluxo com exceção.", "Quando meio é Cartao, chama ...") e a fonte (nó, bloco, faixa, arquivo e linha). A regra do nó (`NodeRule`, Fase 6) tem precedência sobre o texto gerado e vem marcada.
- [x] A mesma condição em sistemas diferentes (sem `this.`, espaços e maiúsculas) vira uma regra só, marcada como duplicada, com uma fonte por sistema. No mesmo sistema, dois `if` iguais continuam sendo duas regras.
- [x] `GET api/surveys/{id}/rules` e a aba "Regras" no painel do mapa, filtrável por efeito, com selo de letra e cor; clicar na fonte escolhe o bloco.
- [x] Testes CORE-RULE-001 a 006 e API-MAP-003; no front, filtro e fonte clicável.

## Fase 33: perguntas sobre o fluxo `[x]` (depende das Fases 30 e 32; IA opcional)

**Status: concluída em 2026-11-04.**

- [x] **Sem IA:** `FlowQuestions.Suggest` monta perguntas por papel (`QuestionRole`) a partir do mapa e das regras: quando o fluxo é recusado, que efeitos tem fora do código, o que acontece se cada chamada falhar (diz se o bloco tem `try/catch`), onde uma regra duplicada está implementada, por quais sistemas passa e quantos caminhos terminam em erro. `GET api/surveys/{id}/questions`.
- [x] **Com IA (provedor):** `POST api/surveys/{id}/ai/ask` (`QuestionAdvisor`). A IA recebe a pergunta, o mapa, as regras e até 8 trechos de código das entradas e decisões, marcados como dado. A resposta passa pela conferência do núcleo (`FlowQuestions.Verify`): fonte que não existe no grafo sai, e resposta sem fonte válida vira "sem resposta no código levantado". A IA nunca carimba "extraída" (só o Rastro faz isso).
- [x] **Decisão:** a pergunta livre usa o uso "Sugestões" da configuração, sem flag nova, para não criar migration nem mudar a tela de configurações. Se o dono quiser um uso próprio, vira item novo.
- [x] Ferramentas do MCP `get_flow_map` e `get_flow_rules` (no lugar dos nomes `rastro_mapa` e `rastro_regras` planejados, para seguir o padrão em inglês das outras ferramentas); as instruções do MCP pedem a resposta citando os nós.
- [x] Sem IA ligada (ou sem o uso "Sugestões"), a tela não mostra a pergunta livre (ADR-001).
- [x] Documentação do cliente: README, README do pacote e `SECURITY.md` dizem o que vai para o provedor numa pergunta e citam as ferramentas novas.
- [x] Testes CORE-ASK-001 a 005, AI-ASK-001 a 003 e MCP-MAP-001 (o mapa e as regras da loja fictícia extraída de verdade, com a chamada do estorno entre os dois sistemas); no front, filtro por papel, selo, lacuna, pergunta livre escondida sem IA, envio e erro por código.
- [ ] Atualizar o contador de tokens da barra logo depois de uma pergunta (hoje só no próximo refresh).

## Fase 34: parâmetros das pontes `[x]` (depende da Fase 30)

**Status: concluída em 2026-11-04.** A parte da tela entrou no commit das Fases 32 e 33 (mesmo painel).

- [x] `HttpCall` ganha `Fields` (campos na ordem de declaração, sem repetidos) e `MoreFields` (havia campos que o extrator não leu); os dois são omitidos no JSON quando vazios, e a igualdade compara `Fields` por valor. `RastroSchema.Version` vai de 2 para 3, e os JSON das versões 1 e 2 continuam sendo lidos.
- [x] C#: nos `*AsJsonAsync`, as propriedades públicas do tipo do corpo (tipo anônimo e herança incluídos); no `PostAsync` e afins, só `JsonContent.Create(x)`. Refit: `[Body]` vira as propriedades do tipo, `[Query]` e o parâmetro de rota entram pelo nome, e parâmetro sem atributo segue a regra do Refit (tipo complexo em POST, PUT e PATCH é corpo). `CancellationToken`, `[Header]` e afins ficam fora.
- [x] Angular: as chaves do objeto literal (propriedade, atalho, método); identificador com tipo declarado vira as propriedades do tipo; no `request`, a propriedade `body` das opções.
- [x] Vira `MoreFields`, nunca some em silêncio: espalhamento, chave calculada, corpo `object`, `dynamic`, `any`, primitivo, coleção, `FormData`, `StringContent` e tipo sem propriedades legíveis. GET, DELETE, `null` e objeto vazio não têm campos.
- [x] Os campos atravessam a costura e a proposta (a aresta é copiada com `with`), conferido nos testes da costura.
- [x] O painel do bloco mostra os campos de cada chamada que sai dele e "e outros campos" quando há `MoreFields`; o rótulo da ligação leva o texto no tooltip.
- [x] Testes EXT-FIELDS-001 a 010, CORE-HTTP-017 e EXT-FIELDS-TS-001 a 007, mais as asserções da costura.
- [ ] Ficam de fora: `SendAsync` (continua sem campos), objeto em `[Query]` (entra o nome do parâmetro), `[AliasAs]` e query string escrita na URL.

## Fase 35: mapa enxuto num fluxo real `[x]` (registrada e aprovada pelo dono em 2026-11-04, no uso da 0.4.0)

No primeiro uso da 0.4.0 num fluxo real (uma tela Angular e duas APIs, 344 passos), o mapa deixou de ser um diagrama C2: 145 blocos (75 num sistema só), 45 decisões, 8 caminhos com aviso de corte e 131 perguntas, 127 delas de Staff. O limite de um terço do trecho para deixar um método transparente não segura APIs com muita lógica interna.

- [x] **Sistemas além do segundo salto entram recolhidos:** o sistema de entrada e os que ele chama direto aparecem abertos; a partir daí, cada sistema chamado vira um bloco só (a entrada, com a rota e o "+N" do que esconde), que abre ao clicar e recolhe de novo pelo painel. `MapBlock` ganha `Segment` (a entrada do trecho) e `Depth` (saltos pelo caminho mais curto). A API e o MCP continuam com o mapa inteiro: quem recolhe é a tela (`visibleMap`, em `core/graph/flow-map.ts`). Selecionar um passo escondido (busca, regra, resposta) abre o que for preciso.
- [x] **Mesma rota, um trecho só:** a chamada repetida para a mesma entrada liga ao trecho já montado, sem abrir de novo, e o caminho passa só pela entrada. O mesmo bloco chamando o mesmo destino pela mesma rota é uma ligação só.
- [x] **Sai a lista de caminhos do topo do mapa:** o destaque fica no clique de um desfecho da decisão, com "Limpar o destaque" na barra, e o aviso de corte sai junto. Os caminhos continuam na API e no MCP.
- [x] **Achado na execução:** a API guarda só 8 caminhos, e num fluxo com muitas decisões a maioria dos desfechos não está em nenhum deles; sem a lista, o clique não acenderia nada. O desfecho fora dos caminhos acende o trecho da entrada até o fim daquele ramo (`outcomeTrail`).
- [x] **Perguntas de falha agrupadas:** uma pergunta "o que acontece se X falhar?" por sistema chamado, por tipo fora do código (`DbContext`, e não cada método) e uma para as chamadas HTTP sem destino, listando os lugares que chamam (até 10, e a contagem do resto).
- [x] Testes: `CORE-MAP-012` a `014`, `CORE-ASK-006` a `008`, `WEB-MAP-010` e `012` a `016`, `WEB-MAPV-002` a `005`, `008`, `013` e `017` a `019`. No repositório público, o fluxo mais pesado (`SurveyWorker.ProcessAsync`, 226 nós) ficou com 6 perguntas de Staff.
- [ ] Aceite no levantamento real que originou a fase: depende de rodar o pacote novo nele, porque o repositório público não tem fluxo com três sistemas. A tela com três saltos foi conferida no navegador com um mapa fictício (prints em `docs/telas/fase-35/`).
- [ ] Rótulo da chamada ("POST api/...") passa por cima dos blocos quando a rota é longa. Já acontecia antes desta fase.

**Ideias registradas no uso, sem fase ainda:**
- [ ] Cenários de teste a partir dos caminhos do mapa (Dado/Quando/Então), com os dados que levam a cada caminho.
- [ ] Caminhos sem teste: ligar os testes do repositório aos nós e marcar no mapa o caminho que nenhum teste percorre.
- [ ] Configuração por ambiente: quando um passo depende de um valor do `appsettings.*.json`, mostrar o valor em cada ambiente e avisar quando divergem.
- [ ] Caminho de uma requisição real: ler um log estruturado e acender no mapa o caminho percorrido pelo id de correlação.
- [ ] Comparação de levantamentos (Fase 12) no nível do mapa: blocos, decisões e pontes novos, removidos ou alterados entre duas branches.
- [ ] Extrator de metadados de CRM (fluxos e gatilhos do Salesforce), para "fora do código" deixar de ser parede. Fase grande, com ADR própria.
- [ ] Serviço próprio que repassa para o `MatDialog` (ficou fora da issue #32): o desenho começa no modal e perde as condições do botão que o abre.

## Visão do banco e análise de consulta (Fases 36 a 42) (pedido do dono em 2026-11-05) `[x]`

**Status: EXECUTADO em 2026-11-06.** As Fases 36 a 42 estão na `main` (a última pelo PR #61). O levantamento de UX do fim do plano e as pendências que ficaram estão em [Fim do plano do banco](#fim-do-plano-do-banco-pendências-consolidadas).

O dono pediu duas funcionalidades novas:
- **Feature 1, visão do banco:** tabelas (campos, tipos, nulos, PK, FK, índices), relacionamentos, views e procedures, com um mapa relacional das tabelas.
- **Feature 2, análise de consulta:** receber uma consulta SQL e apontar onde ela tem gargalo. Já existem ferramentas para isso; a ideia é o Rastro ser mais simples e, com a IA plugada, a IA sugerir a melhora.

**Princípios que valem para as seis fases:**
- **Núcleo sem IA (ADR-001):** esquema, mapa relacional e achados da consulta são determinísticos. A IA só propõe a reescrita e o índice, e a proposta passa por conferência do núcleo antes de aparecer.
- **Primeiro pelo código, depois pelo banco:** a fonte padrão é o que já está no repositório (modelo do EF Core, migrations, arquivos `.sql` e projeto de banco). Conectar num banco vivo é opcional, só de leitura, e muda o ambiente do cliente (ADR nova).
- **Ligação com os fluxos:** o diferencial do Rastro não é ser mais um visualizador de banco, é ligar tabela e consulta ao passo do fluxo que as usa.

**Decisões do dono (aprovadas em 2026-11-06, sobre o protótipo navegável da tela):**
- [x] SQL Server primeiro, com o leitor oficial de T-SQL da Microsoft (`Microsoft.SqlServer.TransactSql.ScriptDom`, licença MIT). PostgreSQL entra neste plano, depois do SQL Server (Fase 42).
- [x] Conexão com banco vivo entra, opcional e só de leitura (catálogo e plano estimado). Sem ela, tudo sai do código.
- [x] A visão do banco é uma tela própria por repositório, com duas abas: "Mapa do banco" e "Analisar consulta".

## Fase 36: ADR da visão do banco `[x]`

- [x] [ADR-006](../adr/adr-006-visao-do-banco.md): fontes do esquema (modelo do EF Core e migrations pelo Roslyn sem rodar o código do cliente, `.sql` e projeto de banco pelo leitor de SQL e, opcional, banco vivo), cada item com a fonte e a divergência marcada; SQL Server primeiro e PostgreSQL na Fase 42, com o modelo e o extrator de `.sql` sem amarrar ao dialeto; o que sai da máquina e o que é guardado.
- [x] Conexão viva: só leitura, nunca os dados das tabelas, comandos fixos ao catálogo, plano estimado sem executar e com a consulta conferida antes pelo leitor de SQL (uma instrução, sem `SET`, `EXEC` nem DDL), timeout, string de conexão cifrada por DataProtection no banco local, nunca na resposta, no log nem na IA. **Implementada na Fase 40**, a primeira que precisa dela.
- [x] `visao.md`, `SECURITY.md` e `CLAUDE.md` (segurança e lista do ambiente do cliente) atualizados. O README e o README do pacote mudam quando o código entrar (Fase 37 em diante).

## Fase 37: esquema pelo código (feature 1, núcleo e API) `[x]` (depende da Fase 36)

- [x] Modelo no `Rastro.Core` (`Database/DbSchema`): tabela, coluna (tipo, nulo, padrão, identidade), chave primária, chave estrangeira (colunas, tabela de destino, ação ao apagar), índice (único, colunas, inclusão), view (colunas e tabelas que ela lê) e procedure (parâmetros e tabelas que ela lê e grava), cada item com a fonte (`SchemaSource`: tipo, arquivo e linha). O dialeto é um campo (`SqlDialect`: SQL Server e PostgreSQL), e o tipo fica como o banco escreve.
- [x] Extrator do EF Core pelo Roslyn, só pela árvore de sintaxe, sem compilar nem rodar o código do cliente: `DbContext`, `DbSet`, atributos, `OnModelCreating`, `IEntityTypeConfiguration`, as convenções do EF (chave, chave estrangeira, nome da tabela, tipo por convenção do SQL Server, identidade) e o `UseSnakeCaseNamingConvention`.
- [x] **Migrations também são fonte:** o `Up` de cada migration, na ordem do id, aplicado num estado só (`CreateTable`, `AddColumn`, `AlterColumn`, `DropColumn`, `RenameColumn`, `CreateIndex`, `AddForeignKey` e o SQL de `migrationBuilder.Sql`). O snapshot e o `.Designer.cs` ficam de fora.
- [x] Extrator de `.sql` e de projeto de banco pelo leitor de T-SQL (`ScriptDom` 180.140.0, MIT, no `Rastro.Extractor` e dentro do pacote): `CREATE TABLE`, `ALTER TABLE ... ADD CONSTRAINT`, `CREATE INDEX`, `CREATE VIEW` e `CREATE PROCEDURE`. Atrás de `ISqlSchemaReader`, um leitor por dialeto: o PostgreSQL entra na Fase 42 como mais um.
- [x] Divergência entre fontes (`DbSchemaMerger`): tipo, nulo, chave primária e coluna que existe no modelo e não na migration (ou o contrário). A coluna divergente fica sem tipo (ou sem nulo), e a divergência mostra cada versão com a fonte.
- [x] `GET api/repositories/{nome}/schema` (pela única porta de disco, `RepositoryCatalog.SourceFiles`, sem `bin`, `obj` e `node_modules`) e a ferramenta MCP `get_db_schema` (o esquema inteiro ou uma tabela), com a instrução no `McpInstructions`. README e README do pacote atualizados.
- [x] Testes com a loja fictícia `Fixtures/LojaBanco` (os dados do protótipo: clientes, pedidos, itens com PK composta, pagamentos com FK sem índice, produtos vindo de `.sql`, tabela sem PK, views, procedure e o `status` com 20 no modelo e 30 na migration): `EXT-DB-001` a `013`, `CORE-DB-001` a `007`, `API-DB-001` e `002` e `MCP-DB-001`.
- [x] Índice que o EF cria por convenção em cada chave estrangeira: deduzido do modelo e marcado (`DbIndex.Convention`) na Fase 38. Só fica quando a tabela não tem migration nem `.sql`; com eles, a FK que eles criam sem índice vira achado.
- [ ] Argumento posicional nas migrations e configuração em método auxiliar (`ConfigurarPedido(modelBuilder)`) não são lidos; o EF gera argumentos com nome, então só código escrito à mão sofre.
- [ ] Divergência de chave estrangeira e de índice entre fontes (hoje só a primeira fonte fica, sem marcar).

## Fase 38: mapa relacional e tela do banco (feature 1, tela) `[x]` (depende da Fase 37)

- [x] Tela própria por repositório, `/repositorios/{nome}/banco` ("Mapa do banco"), com entrada pelo link "Ver o mapa do banco" no levantamento novo e pelo "Banco" na barra do levantamento, troca de repositório na barra e a promessa "Lido do código. Nenhum banco é consultado". A aba "Analisar consulta" do protótipo entra com a Fase 40, para não deixar aba morta.
- [x] Mapa relacional no desenho do Rastro (`core/database/db-map.ts`): tabelas como blocos com PK e FK marcadas (texto além da cor), em colunas pela FK (quem é apontado à esquerda), com a seta saindo da linha da coluna da FK até a chave de destino e a cardinalidade no meio (1 : N, 1 : 1). A tabela de ligação aparece no painel como N para N. Tipo divergente marcado no bloco. Zoom de 50% a 150%.
- [x] Lista com busca (por nome e por campo) de tabelas, views e procedures; painel da tabela com fontes, campos (tipo, nulo, chave, identidade, padrão), índices (com o selo "convenção do EF"), relações clicáveis (para onde aponta, quem aponta para ela, ação ao apagar, views e procedures que a usam), divergência entre fontes e achados sem IA. A view e a procedure escolhidas acendem as tabelas que leem e gravam.
- [x] Recorte por tabela: a tabela escolhida e as vizinhas até 1, 2 ou 3 saltos, com a contagem do que ficou fora.
- [x] Exportar o que está no mapa (o recorte, se houver) em Mermaid `erDiagram` (copiar e baixar `.mmd`), PNG e SVG.
- [x] **Melhorias propostas que entraram:** achado "chave estrangeira sem índice" (`SchemaFinding`, só quando a migration ou o `.sql` cria a FK sem um índice que comece pelas colunas dela; o índice da convenção do EF não dá falso positivo) e a marca "pessoal" pelo nome da coluna (`PersonalData`, palavra inteira: CPF, e-mail, telefone, nascimento, endereço, senha; "nome" fica de fora). Os dois também no texto do `get_db_schema`.
- [x] Testes: `EXT-DB-014` a `016`, `CORE-DB-008` e `009`, `MCP-DB-001` (achados e dado pessoal), `WEB-DB-001` a `008` e `WEB-DBV-001` a `008`. Backend 455/455 e frontend 290/290.
- [x] Prints em `docs/telas/fase-38/` (loja fictícia, API local com `RASTRO_HOME` temporário) e levantamento de UX em [`plano-ux-2026-11-06-tela-do-banco.md`](../frontend/plano-ux-2026-11-06-tela-do-banco.md): os achados de risco baixo e médio entraram na fase.
- [x] (UX, risco alto) Entrada "Banco" no cabeçalho do Rastro, ao lado das configurações, sem depender de escolher o repositório antes. Feito na repaginação de UX, Fase C (2026-11-06).
- [ ] Banco grande: com centenas de tabelas, o desenho fica largo (colunas de 8 tabelas). Avaliar enquadrar na tela e um minimapa quando aparecer um caso real.

## Fase 39: banco nos fluxos `[x]` (depende da Fase 37)

- [x] O passo diz qual tabela lê ou grava (`FlowNode.Db`, `DbAccess`: tabela ou procedure, operação e linha), pelo `DbSet` (ou `Set<T>`) e pelo método do EF da cadeia (consulta, inclusão, alteração, exclusão, `ExecuteUpdate` e `ExecuteDelete`), pelo `Add`, `Update` e `Remove` do `DbContext` e pela procedure com `EXEC` em `FromSql`, `ExecuteSql` e `SqlQuery` (execução). `DbAccessInspector` no `Rastro.Extractor`, sem IA. A tabela sai do esquema do repositório inteiro (`ExtractionOptions.Schema`, pelo `DbSchemaService` e pela porta única de disco), ligada à entidade por `DbTable.Entity`; sem esquema, fica o nome do `DbSet`. O passo com EF ganha a etiqueta "banco" mesmo que o nome não diga, e o painel do passo mostra "No banco" com o link para a tabela no mapa (`?tabela=`).
- [x] No painel da tabela e da procedure, "Fluxos que usam esta tabela" e "Fluxos que chamam esta procedure": a operação, o fluxo e o passo (link para `/surveys/{id}?passo=`), do levantamento mais novo de cada método, só o trecho do próprio repositório. Rota nova `GET api/repositories/{nome}/schema/usage` (`DbUsageReport`: usos e achados). Levantamento anterior à fase não tem a informação, e o painel diz isso.
- [x] Achados sem IA (categoria "Banco de dados", com `Finding.Target` para a tela do banco): consulta dentro de laço (N+1, direta ou por um método do fluxo que consulta; a coleção do `foreach` não conta), `ToList` antes do filtro (na cadeia ou na variável que guarda a lista), leitura sem `AsNoTracking` só em método que não grava e sem projeção, e procedure por SQL cru (avisa quando ela não está nos `.sql`). O prompt de refatoração ganhou a orientação e o critério de aceite de cada um.
- [x] Testes: `EXT-DBF-001` a `007` (fixture `LojaFluxo`, com um pedaço do EF Core nos mesmos nomes), `API-DB-003`, `WEB-DBF-001` e `002` e `WEB-DBV-009` a `011`. Backend 463/463 e frontend 295/295.
- [x] Prints em `docs/telas/fase-39/` (loja fictícia, dois levantamentos de verdade pela API local com `RASTRO_HOME` temporário) e levantamento de UX em [`plano-ux-2026-11-06-tela-do-banco.md`](../frontend/plano-ux-2026-11-06-tela-do-banco.md) (seção da Fase 39).
- [ ] Escrever a tabela no próprio bloco do mapa do fluxo e no nó do grafo. Hoje: a etiqueta "banco" com a dica e o rótulo de acessibilidade ("Lê pedidos") e o painel do passo.
- [ ] Dapper e ADO.NET: a tabela pelo texto do SQL (leitor de T-SQL), como a procedure.
- [ ] Alteração por rastreamento (entidade lida, alterada e gravada no `SaveChanges`) aparece como leitura; ligar a escrita à tabela pela entidade alterada.

## Fase 40: análise de consulta sem IA (feature 2, núcleo, API e tela) `[x]` (depende da Fase 37)

Dividida em dois PRs: 40a (análise pelo código) e 40b (banco vivo).

### Fase 40a: análise pelo código `[x]`

- [x] Entrada: a consulta colada na tela ou vinda de um passo do fluxo (o passo com SQL cru literal, `FromSql`, `FromSqlRaw`, `ExecuteSqlRaw` e `SqlQuery`, guarda o texto em `DbAccess.Sql`, com `{0}` e os buracos da interpolação trocados por `@p0`; o painel do passo ganha "Analisar a consulta") e o repositório, para cruzar com o esquema.
- [x] Achados determinísticos pelo leitor de T-SQL (`TSqlQueryAnalyzer` no `Rastro.Extractor`, atrás de `ISqlQueryAnalyzer`, um por dialeto; modelo `QueryAnalysis` no `Rastro.Core`), cada um com a gravidade, o trecho (posição e tamanho no texto), a linha e o motivo: `SELECT *` (com as colunas de dado pessoal), função ou conversão na coluna do filtro (com "e a coluna nem tem índice"), `LIKE '%...'` (com o índice que existe e não ajuda), `OR` entre colunas diferentes, filtro ou junção em coluna sem índice que comece por ela (cruzando com o esquema), junção sem condição (`CROSS JOIN`, vírgula sem ligação no `WHERE`, `ON` sem coluna das duas tabelas), `DISTINCT` numa consulta com junção, subconsulta correlacionada (na lista do `SELECT` e na comparação; `EXISTS` não conta), `ORDER BY` sem `TOP` nem `OFFSET`, `NOT IN` de subconsulta com coluna que aceita nulo e `UPDATE` ou `DELETE` sem `WHERE` (com junção no `FROM`, não conta). Tabela fora do esquema aparece marcada, sem os achados de índice e de nulo.
- [x] `POST api/repositories/{nome}/schema/analyze` (`{ sql }`, até 100 mil caracteres, erro `QUERY_INVALID`) e a ferramenta MCP `analyze_query` (só os achados sem IA; a proposta da IA é da Fase 41), com a instrução no `McpInstructions`. O texto da consulta não vai para o log.
- [x] Tela: abas "Mapa do banco" e "Analisar consulta" (`?aba=consulta`, com `consulta` e `origem` vindos do passo), a consulta com os trechos marcados (sublinhado na cor da gravidade), os achados por gravidade com o rótulo em texto, clique que destaca o trecho, "Tabelas da consulta" com o link para o mapa e "ver no mapa" no achado. Sem botão de IA.
- [x] Testes: `EXT-DBQ-001` a `015` (uma consulta da loja fictícia por achado, a do protótipo, a limpa, o erro de leitura e a tabela fora do esquema), `EXT-DBF-008`, `API-DBQ-001` e `002`, `MCP-DBQ-001`, `WEB-DBQ-001` a `004`, `010` a `018`, `020` a `022` e `030`. Backend 482/482 e frontend 312/312.
- [x] Prints em `docs/telas/fase-40/` (loja fictícia, API local com `RASTRO_HOME` temporário, clique real) e levantamento de UX em [`plano-ux-2026-11-06-tela-do-banco.md`](../frontend/plano-ux-2026-11-06-tela-do-banco.md) (seção da Fase 40a): os achados de risco baixo e médio entraram na fase.
- [ ] `ExecuteSql` e `SqlQuery` com SQL que não é `EXEC` não viram acesso do passo (não há tabela para ligar sem ler o texto); ler a tabela pelo leitor de T-SQL junto com Dapper e ADO.NET (pendência da Fase 39).
- [x] (UX, risco alto) Analisar sozinho quem chega pelo "Analisar a consulta" do passo (UX-DBQ-05). Feito nos [acréscimos de UX de 2026-11-07](../frontend/plano-ux-2026-11-07-acrescimos.md), Fase E.
- [ ] Consulta montada em partes no código (concatenação, `StringBuilder`) não vai para o passo: só o literal.

### Fase 40b: banco vivo `[x]`

- [x] Conexão viva (ADR-006): cadastro da string de conexão por repositório na tela (diálogo "Banco vivo", campo de senha que nunca volta preenchido), cifrada pelo DataProtection com propósito próprio na tabela `live_connections` (migration `BancoVivo`), nunca devolvida na resposta, no log nem para a IA; a tela mostra só o servidor e o banco. Rotas `GET`, `PUT` e `DELETE api/repositories/{nome}/connection` e `POST .../connection/test`, com `AllowedHosts` e `Origin` valendo.
- [x] `Microsoft.Data.SqlClient` 6.1.1 no pacote (com o `System.Security.Cryptography.Pkcs` fixado na versão corrigida), comandos fixos ao catálogo, conexão sem pool, com tempo limite e o nome "Rastro (só leitura)". O catálogo entra como mais uma fonte do esquema (`SchemaSourceKind.LiveDatabase`); banco fora do ar vira nota e o esquema sai do código.
- [x] Plano estimado (`POST api/repositories/{nome}/schema/plan`): `TSqlPlanGuard` confere antes que é uma instrução só, `SELECT`, `INSERT`, `UPDATE`, `DELETE` ou `MERGE`, sem `SET`, `EXEC`, DDL, `GO`, `SELECT INTO`, `OUTPUT INTO` nem `OPENROWSET`, `OPENQUERY` e `OPENDATASOURCE`; as variáveis entram declaradas como texto; `SET SHOWPLAN_XML ON` e o `ShowPlanReader` tiram as operações mais caras (custo próprio), as varreduras, o índice que falta segundo o banco e os avisos. Códigos `CONNECTION_INVALID`, `CONNECTION_MISSING`, `PLAN_NOT_ALLOWED` e `LIVE_DATABASE_ERROR`, com o motivo em pt-BR e nunca a mensagem crua do driver.
- [x] Tela: selo "Banco vivo: servidor/banco, só leitura" e a promessa trocada, o diálogo com testar e remover (confirmação no próprio diálogo) e a recomendação de permissão mínima, e o cartão "Plano estimado pelo banco" na aba de consulta, pedido pelo botão.
- [x] **Correção da ADR-006 descoberta no navegador:** o SQL Server só mostra o plano a quem pode rodar a instrução, então o plano pede `SHOWPLAN` e o `SELECT` nas tabelas da consulta, além do `VIEW DEFINITION` do catálogo. ADR, README, README do pacote, `SECURITY.md` (sem o "em construção") e o texto da tela corrigidos; o erro 229 diz isso na tela.
- [x] Hook `ambiente-cliente.mjs` com o padrão do código da conexão (`Rastro.Infrastructure/Database/Live/`) e do `.csproj` das bibliotecas do pacote, e o teste dele (`ambiente-cliente.test.mjs`). Job novo `banco-vivo` no workflow de PR (Linux, que tem Docker com contêiner Linux).
- [x] Testes: `EXT-DBP-001` e `002` (a porta da consulta), `INF-DBL-001` e `002` (leitura do plano e tipos do catálogo), `API-DBL-001` a `005` (com o banco de mentira) e `INF-DBL-010` a `012` com SQL Server de verdade no Testcontainers (catálogo, plano da consulta do protótipo e o plano de um `DELETE` que não apaga nada; pulados com o motivo onde o Docker não roda contêiner Linux). Backend 504/504 e frontend 324/324.
- [x] Prints em `docs/telas/fase-40/` (SQL Server descartável em contêiner, usuário só de leitura, API local com `RASTRO_HOME` temporário) e levantamento de UX (seção da Fase 40b).
- [ ] Banco vivo pelo MCP (`get_db_schema` com o catálogo e o plano no `analyze_query`): hoje só pela tela e pela API, porque o MCP não registra o DataProtection.

## Fase 41: a IA sugere a melhora (feature 2, IA plugada) `[x]` (depende da Fase 40)

- [x] Provedor e agente (MCP `analyze_query`) recebem a consulta, os achados e só as tabelas que ela usa, marcados como dado; a IA devolve a consulta reescrita e os índices sugeridos, com o motivo de cada mudança. O recorte sai do núcleo, sem IA (`QueryContextText` no `Rastro.Core`: colunas, tipos, nulo, chaves e índices só das tabelas da consulta). Provedor: `QueryAdvisor` no `Rastro.Ai` (uso "Sugestões", sem flag nova), com a consulta, os achados e as tabelas entre `<consulta>`, `<achados>` e `<tabelas>` e a instrução de ignorar ordens dentro deles; rota `POST api/repositories/{nome}/schema/ai/suggestion`. Agente: o `analyze_query` passa a levar o esquema das tabelas da consulta, e a ferramenta nova `propose_query_rewrite` entrega a proposta; não há ferramenta para aplicar nem para confirmar.
- [x] Conferência do núcleo antes de mostrar (`TSqlProposalChecker` no `Rastro.Extractor`, atrás de `ISqlProposalChecker`, um por dialeto; modelo `QueryProposal` e `QueryProposalReview` no `Rastro.Core`; porta única `QueryProposalService` na infraestrutura, a mesma do provedor e do agente): a reescrita precisa ser lida pelo leitor de T-SQL, passar pela porta do banco vivo (`TSqlPlanGuard`: uma instrução, sem `SET`, `EXEC` nem DDL), fazer a mesma operação na mesma tabela, citar só tabelas e colunas que existem (ou que a original já citava; a parte da data em `DATEADD` e afins não conta como coluna), devolver as mesmas colunas, com os mesmos nomes e na mesma ordem (o `SELECT *` é expandido pelo esquema), e não limitar as linhas (`TOP`, `OFFSET`) que a original não limita. O índice só usa colunas que existem, não vai em view e não repete um índice nem a chave primária que já começa pelas mesmas colunas; o `CREATE INDEX` é escrito pelo Rastro. O que não passa volta recusado com o motivo, e a reescrita aceita ganha os achados sem IA dela.
- [x] Com banco vivo: o custo estimado do plano antes e depois (`LiveDatabaseService.ComparePlansAsync`, dois pedidos de `SHOWPLAN_XML`), só para a reescrita aceita, sem executar nada.
- [x] Tela: seção "Sugestão da IA" abaixo dos achados, só com a IA e o uso "Sugestões" ligados (ADR-001), com o que vai para o provedor escrito ao lado do botão. Antes e depois lado a lado, "Copiar a consulta reescrita", os achados antes e depois, o custo, as mudanças com o motivo e os índices aceitos (com o comando e "Copiar") e recusados (com o motivo). Nenhum botão de aplicar: quem copia é o usuário.
- [x] README, README do pacote, `SECURITY.md`, ADR-006, `visao.md` e as instruções do MCP: o que vai para o provedor numa análise (a consulta com os literais, os achados e só as tabelas que ela usa; nunca a string de conexão nem os dados das tabelas) e a conferência. O texto da consulta não vai para o log.
- [x] Testes: `EXT-DBA-001` a `012`, `API-DBA-001` a `003` (provedor falso e banco vivo de mentira), `MCP-DBA-001` e `002` e `WEB-DBA-001` a `009`. Backend 521/521 e frontend 333/333.
- [x] Prints em `docs/telas/fase-41/` (loja fictícia, API local com `RASTRO_HOME` temporário; erro com chave falsa de teste; reescrita aceita e recusada com a resposta da IA simulada e conferida pelo núcleo de verdade) e levantamento de UX em [`plano-ux-2026-11-06-tela-do-banco.md`](../frontend/plano-ux-2026-11-06-tela-do-banco.md) (seção da Fase 41): os achados de risco baixo e médio da tela entraram na fase.
- [x] (UX-DBA-06, risco médio, todas as telas de IA) Mostrar o motivo que o Rastro escreve no erro do provedor (chave inválida, limite de uso) em vez de "O provedor de IA não respondeu", e tirar "nó" das mensagens dos provedores. Feito nos [acréscimos de UX de 2026-11-07](../frontend/plano-ux-2026-11-07-acrescimos.md), Fase E.
- [ ] Sugestão da IA pelo agente com o custo antes e depois: o MCP não tem banco vivo (pendência da Fase 40b).
- [ ] A conferência não prova que a reescrita devolve as mesmas linhas (só a mesma operação, a mesma tabela, as mesmas colunas e o mesmo limite); a tela diz "conferida pelo Rastro", e quem decide é o usuário.

## Fase 42: PostgreSQL `[x]` (depende das Fases 37 a 41)

Dividida em dois PRs: 42a (código: extrator, achados e conferência) e 42b (banco vivo).

### Fase 42a: PostgreSQL pelo código `[x]`

- [x] Leitor de SQL do PostgreSQL escolhido e registrado na [ADR-006](../adr/adr-006-visao-do-banco.md): **SqlParserCS 0.6.5** (MIT, código gerenciado, sem biblioteca nativa, no `Rastro.Extractor` e dentro do pacote). Descartados o `libpg_query` (`pgsqlparser`, `Npgquery`, `PgQuery`: nativo, uma DLL por sistema), a gramática do ANTLR (lenta, Java no build) e o leitor comercial. O leitor não dá a posição de cada nó: o trecho sai dos tokens (`PgSqlText.Locate`). Defeito do leitor contornado: no dialeto do PostgreSQL, o padrão do `LIKE` engolia o `AND` seguinte (`FixedPostgreSqlDialect`, `EXT-PGQ-018`).
- [x] Extrator de `.sql` do PostgreSQL (`PgSqlSchemaReader`): `CREATE TABLE` (com `serial`, identidade e as chaves na coluna ou na tabela), `ALTER TABLE ... ADD` (chave, coluna, a identidade à parte do `pg_dump`), `CREATE INDEX`, `CREATE VIEW` (e a materializada), `CREATE FUNCTION` e `CREATE PROCEDURE` pelos tokens (parâmetros com o modo e as tabelas que o corpo em PL/pgSQL lê e grava). Cada instrução é lida sozinha (a que falha vira nota), e a carga do `COPY ... FROM stdin` é ignorada. A chave sem nome escrito ganha o nome do PostgreSQL (`tabela_pkey`).
- [x] Tipos e nomes do Npgsql conferidos: o mapa de tipos por convenção (`text`, `character varying(n)`, `timestamp with time zone`, `uuid`, `jsonb`, `numeric`, `bytea`, vetores), `JsonDocument` e `string[]` como coluna, a identidade por `UseIdentityByDefaultColumn`, `UseIdentityAlwaysColumn`, `UseSerialColumn` e pela anotação `Npgsql:ValueGenerationStrategy` da migration, e os sinônimos (`int` e `integer`, `varchar` e `character varying`, `timestamptz`) comparados pelo nome do catálogo, sem divergência falsa. Com o `UseSnakeCaseNamingConvention`, a chave, a FK e o índice por convenção ganham o nome em minúsculas (`pk_pedidos`), nos dois dialetos.
- [x] O dialeto sai do repositório (`DbSchemaExtractor.DialectOf`): o provedor do EF, as anotações das migrations e, sem EF, as marcas dos `.sql` pelos tokens.
- [x] Achados da análise de consulta no PostgreSQL (`PgSqlQueryAnalyzer`): as regras da Fase 40, com `LIMIT` ou `FETCH` no lugar do `TOP`, o `UPDATE ... FROM` e o `DELETE ... USING` como junção e o `LEFT JOIN LATERAL` na subconsulta correlacionada, mais o `ILIKE` com curinga no começo (com a sugestão do índice trigrama) e a conversão com `::` na coluna do filtro. `$1` e `@nome` são parâmetros.
- [x] A conferência da proposta da IA no PostgreSQL (`PgSqlProposalChecker`) e a porta da consulta para o banco vivo (`PgSqlPlanGuard`: uma instrução só, sem `SELECT INTO`, sem escrita dentro de outra instrução e sem função de efeito colateral conhecida). O provedor recebe o texto do sistema no dialeto, e o `CREATE INDEX` sai com aspas duplas e o nome em até 63 caracteres. Ferramentas e instruções do MCP falam dos dois dialetos.
- [x] Tela: o rótulo da caixa e o erro de leitura no dialeto do repositório; os títulos "Função ou conversão na coluna do filtro" e "Curinga no começo do LIKE ou ILIKE".
- [x] Loja fictícia `Fixtures/LojaBancoPg` (a mesma loja no Npgsql, com `.sql` do PostgreSQL, função, procedure, trecho do `pg_dump` e arquivo quebrado). Testes: `EXT-PG-001` a `012`, `EXT-PGQ-001` a `018`, `EXT-PGA-001` a `006`, `EXT-PGP-001` e `002`, `API-PG-001` a `003`, `MCP-PG-001` e `WEB-PG-001`.
- [x] Prints em `docs/telas/fase-42/` (loja fictícia, API local com `RASTRO_HOME` temporário, clique real) e levantamento de UX em [`plano-ux-2026-11-06-tela-do-banco.md`](../frontend/plano-ux-2026-11-06-tela-do-banco.md) (seção da Fase 42a).
- [x] (UX-PG-05, risco médio) Traduzir as mensagens mais comuns dos leitores de SQL (hoje em inglês, nos dois dialetos). Feito nos [acréscimos de UX de 2026-11-07](../frontend/plano-ux-2026-11-07-acrescimos.md), Fase E.
- [ ] Corpo de função em `BEGIN ATOMIC` (PostgreSQL 14) e tipo com precisão no meio (`timestamp(3) with time zone`) não são lidos por inteiro.
- [ ] Relatar ao SqlParserCS o defeito da precedência do `LIKE` e tirar o contorno quando a versão corrigida sair.

### Fase 42b: banco vivo do PostgreSQL `[x]`

- [x] Driver oficial `Npgsql` 10.0.3 (licença PostgreSQL, gerenciado) no pacote. `NpgsqlLiveDatabase`, ao lado do `SqlServerLiveDatabase`, atrás do mesmo `ILiveDatabase` (agora com o dialeto); o `LiveDatabaseService` escolhe o driver, a porta (`SqlPlanGuards`) e a leitura do plano pelo dialeto da conexão.
- [x] Dialeto da conexão sem perguntar (`LiveConnectionDialect`): as chaves que só um driver aceita e, na string que serve aos dois, o dialeto do código do repositório (`DbSchemaService.DialectAsync`). Guardado com a conexão (migration `BancoVivoDialeto`, coluna `dialect`; as conexões de antes ficam como SQL Server) e devolvido na tela (`LiveConnectionView.Dialect`). A URL `postgresql://` é recusada com o formato certo.
- [x] Catálogo pelo `pg_catalog`, com comandos fixos: tabelas (sem partição), colunas pelo `format_type`, nulo, padrão, identidade e `serial`, chave primária, índices com as colunas incluídas, chaves estrangeiras com a ação ao apagar, views (e a materializada) e as funções e procedures do usuário, sem as de extensão.
- [x] Plano estimado por `EXPLAIN (FORMAT JSON)` sem `ANALYZE` (`ExplainPlanReader`: custo próprio, porcentagem e o `Seq Scan` como varredura). Camadas: a porta do PostgreSQL, a conexão com `default_transaction_read_only` e `statement_timeout`, sem pool e com tempo limite, a transação `READ ONLY` que sempre volta atrás e o lote de um comando só. Com parâmetro, `PREPARE` e o plano genérico (`plan_cache_mode = force_generic_plan`, PostgreSQL 12 em diante); o `@nome` vira `$n` pelos tokens (`PgSqlPlanGuard.Positional`). Erros pelo SQLSTATE, em pt-BR, nunca a mensagem crua do servidor.
- [x] Tela: o diálogo com o dialeto da conexão, a permissão mínima do PostgreSQL e o modelo da string; o cartão do plano com a nota do `EXPLAIN` e os operadores do PostgreSQL em pt-BR. O erro do plano diz "O plano não veio do banco vivo" (antes, "não respondeu", também quando o banco respondeu recusando).
- [x] CI: o job `banco-vivo` roda também os testes do PostgreSQL no Testcontainers.
- [x] Testes: `INF-PGL-001` a `003` e `EXT-PGP-003` (sem banco), `API-PGL-001` e `002` (banco de mentira do dialeto), `INF-PGL-010` a `014` com PostgreSQL 17 de verdade no Testcontainers (catálogo, plano com parâmetro, `EXPLAIN` de `INSERT`, `UPDATE` e `DELETE` que não muda nenhuma linha, duas instruções e função que grava recusadas pelo banco, usuário só de leitura sem permissão de `DELETE`), `WEB-PGL-001` e `002`.
- [x] Prints em `docs/telas/fase-42/` (PostgreSQL descartável em contêiner com usuário só de leitura, API local com `RASTRO_HOME` temporário, clique real) e levantamento de UX (seção da Fase 42b).
- [ ] O PostgreSQL não aponta índice faltando: avaliar a sugestão de índice pelo plano (o `Seq Scan` com filtro) junto com os achados sem IA.
- [ ] Banco vivo pelo MCP continua pendente (Fase 40b), agora nos dois dialetos.
- [x] (UX-PGL-05, risco médio) A permissão mínima do diálogo em lista, um comando por linha, com "Copiar". Feito nos [acréscimos de UX de 2026-11-07](../frontend/plano-ux-2026-11-07-acrescimos.md), Fase E.

**Melhorias propostas na análise do pedido (para o dono decidir):**
- [x] **O diferencial é a ligação com o código:** a ferramenta de banco mostra a tabela; o Rastro mostra quem a usa e por qual fluxo (Fase 39, feita logo depois da 38).
- [ ] **Impacto de mudança no banco:** antes de mudar uma coluna, listar os fluxos, views e procedures que dependem dela, usando o grafo que já existe.
- [ ] **Validação da mudança (Fase 9) olhando o banco:** o rascunho que mexe em consulta mostra os achados da Fase 40 antes e depois.
- [ ] **Comparar esquema entre branches**, como a comparação de levantamentos: tabela, coluna e índice novos ou removidos.
- [x] **Migration sem índice na FK nova:** achado automático quando uma migration cria FK sem índice (Fase 38, vale também para o `.sql`).
- [x] **Dados sensíveis:** marcar colunas com nome de dado pessoal (CPF, e-mail, telefone) no mapa, útil para LGPD, sem ler nenhum dado (Fase 38).
- [x] **Nunca ler dados das tabelas:** só catálogo e plano estimado, escrito na ADR-006 e no `SECURITY.md` (Fase 40b).

## Fim do plano do banco: pendências consolidadas

Levantamento de UX completo do fim do plano em [`plano-ux-2026-11-06-fim-plano-banco.md`](../frontend/plano-ux-2026-11-06-fim-plano-banco.md): 54 achados (33 de risco baixo, 18 de risco médio e 3 de risco alto), em cinco fases. A repaginação de UX (2026-11-06) já fez 16 dos 54 (entre eles o UX-DB-09, a entrada "Banco" no cabeçalho, e o campo antes das permissões no diálogo do banco vivo); o resto espera a aprovação do dono. Os itens abaixo são os `[ ]` das Fases 36 a 42, agrupados, e continuam registrados na própria fase.

**Esperam a aprovação do dono (UX):**
- [x] Entrada "Banco" no cabeçalho, sem escolher o repositório antes (UX-DB-09, Fase 38, risco alto). Feito na repaginação de UX, Fase C (2026-11-06).
- [x] Analisar sozinho quem chega pelo "Analisar a consulta" do passo (UX-DBQ-05, Fase 40a, risco alto). Feito nos [acréscimos de UX de 2026-11-07](../frontend/plano-ux-2026-11-07-acrescimos.md), Fase E.
- [x] O motivo do erro do provedor em todas as telas de IA, sem "nó" nas mensagens (UX-DBA-06, Fase 41). Feito nos [acréscimos de UX de 2026-11-07](../frontend/plano-ux-2026-11-07-acrescimos.md), Fase E.
- [x] As mensagens mais comuns dos leitores de SQL em pt-BR, nos dois dialetos (UX-PG-05, Fase 42a). Feito nos [acréscimos de UX de 2026-11-07](../frontend/plano-ux-2026-11-07-acrescimos.md), Fase E.
- [x] A permissão mínima do diálogo "Banco vivo" em lista, um comando por linha, com "Copiar" (UX-PGL-05, Fase 42b). Feito nos [acréscimos de UX de 2026-11-07](../frontend/plano-ux-2026-11-07-acrescimos.md), Fase E.
- [ ] As Fases A a E do levantamento do fim do plano (a Fase E, item a item). Em 2026-11-07, 16 dos 54 achados já estavam feitos pela repaginação de UX (Fases A, B e C); restam 38, listados em [`plano-ux-2026-11-06-repaginacao.md`](../frontend/plano-ux-2026-11-06-repaginacao.md), seção "Planos de UX anteriores depois da repaginação".

**Leitura do código:**
- [ ] Argumento posicional nas migrations e configuração em método auxiliar (Fase 37).
- [ ] Divergência de chave estrangeira e de índice entre fontes (Fase 37).
- [ ] A tabela pelo texto do SQL em Dapper, ADO.NET e no `ExecuteSql` e `SqlQuery` que não são `EXEC` (Fases 39 e 40a).
- [ ] Alteração por rastreamento ligada à escrita da tabela (Fase 39).
- [ ] Consulta montada em partes no código (Fase 40a).
- [ ] `BEGIN ATOMIC` e tipo com precisão no meio no PostgreSQL; relatar o defeito do `LIKE` ao SqlParserCS (Fase 42a).

**Banco vivo, MCP e tela:**
- [ ] Banco vivo pelo MCP nos dois dialetos, e com ele o custo antes e depois na sugestão pelo agente (Fases 40b, 41 e 42b).
- [ ] Índice faltando no PostgreSQL, pelo plano (Fase 42b).
- [ ] A tabela no bloco do mapa do fluxo e no nó do grafo (Fase 39).
- [ ] Banco com centenas de tabelas: enquadrar e minimapa, quando aparecer um caso real (Fase 38).
- [ ] Limite registrado: a conferência da reescrita não prova que ela devolve as mesmas linhas (Fase 41).

**Melhorias propostas, sem decisão:** impacto de mudança no banco, validação da mudança olhando o banco e comparação de esquema entre branches (lista da Fase 42).

## Fase 43: correções do teste num sistema real `[x]` (pedido do dono em 2026-11-06)

O dono pediu para testar com o Rastro todas as consultas de uma tela de tarefas de um sistema dele (PostgreSQL com EF Core, 87 tabelas, 36 consultas). O teste mostrou 13 defeitos e lacunas do Rastro. Depois das correções, o mesmo teste roda de novo.

**Fase 43a, análise de consulta e mapa do banco:** `[x]`
- [x] "Coluna sem índice" aceita o índice composto cujas colunas iniciais estão todas no filtro com igualdade (`tenant_id = @t AND space_id = @s` com o índice `(tenant_id, space_id)`), e a mensagem deixa de afirmar como fato que o banco lê a tabela inteira.
- [x] O trecho marcado aponta a coluna no predicado (`WHERE`, `ON`), não a primeira menção dela na lista do `SELECT`.
- [x] O mapa lê índice com classe de operador e método (`USING gin (title gin_trgm_ops)`) no SQL de `migrationBuilder.Sql`; o achado de `LIKE` com curinga no começo não sugere criar o índice trigrama que já existe.
- [x] Propriedade de tipo complexo com `HasColumnType("jsonb")` (ou `json`) é coluna: sem a divergência falsa "existe na migration, não no modelo".
- [x] Regra nova: leitura sem `LIMIT` (`TOP`, `FETCH`) de tabela filtrada só por igualdade de uma coluna que não é chave (o tenant, por exemplo), de gravidade baixa. "ORDER BY sem LIMIT" deixa de disparar quando o filtro já é pela chave estrangeira de uma lista filha.
- [x] Mensagens: OR na mesma coluna sugere `IN`, não "colunas diferentes" com `UNION ALL`; o operador jsonb `->>` não é chamado de "conta"; a forma que o EF gera para `!Contains` não vira "impede índice em t.id"; `ILIKE` e `= ANY` sobre coluna array (`unnest`) são apontados como sem índice B-tree.
- [x] Subconsulta correlacionada que bate na chave primária da tabela de dentro cai para gravidade baixa.
- [x] As duas escalas de gravidade ficam no mesmo sentido: `QuerySeverity` passa a `Low = 0`, `Medium = 1`, `High = 2`, como o `FindingSeverity` (`Info = 0`). A v0.6.0, com a escala antiga, não chegou ao NuGet.
- [x] Testes `EXT-PGQ-019` a `030`, `EXT-DBQ-016` a `019` e `EXT-PG-040` a `043`, com a loja fictícia de vários inquilinos `Fixtures/LojaMultiTenantPg`, e `WEB-DBQ-005` e `031` no frontend. Decisões: com índice trigrama (GIN ou GiST) na coluna, o `ILIKE '%x%'` não é achado; OR na mesma coluna não é achado; a coluna de lista (`unnest`, `ANY(coluna)`) usa o achado de função na coluna, com texto próprio; a lista filha pela chave estrangeira fica fora da leitura sem limite e do `ORDER BY` sem limite, menos a chave de uma tabela apontada por três ou mais (o tenant, o usuário).

**Fase 43b, banco nos fluxos:** `[x]`
- [x] Repositório genérico (`EfRepository<T>` com `DbSet<T>`): o `T` é fechado pela chamada (`IRepository<TaskItem>`), a tabela sai pelo tipo e "DbSet" deixa de aparecer como tabela; o `Update` e o `Delete` do repositório genérico contam como alteração.
- [x] Chamada pela interface que um tipo reimplementa (`new` sobre o método da base genérica) resolve para a implementação certa pelas regras do C#, sem ficar "não resolvido" com dois candidatos.
- [x] Com o repositório genérico resolvido, o N+1 dentro de laço aparece também quando a consulta está no repositório.
- [x] "ToList antes do filtro" entre métodos: o repositório devolve a lista inteira e o chamador aplica `Where` em memória.
- [x] "Count depois de ToListAsync" não dispara quando o `Count` é a propriedade da lista carregada para `RemoveRange`; nesse caso, a dica é `ExecuteDeleteAsync`.
- [x] O padrão de `LIKE` montado no código (`$"%{x}%"`) aparece no passo do fluxo ligado à consulta.
- [x] Testes `EXT-DBF-009` a `017` com a fixture fictícia `Fixtures/TarefasRepo`. Achados novos: `LoadToDelete` (carrega a lista só para o `RemoveRange`; a dica é `ExecuteDeleteAsync`) e `LeadingWildcardLike` (o `LIKE` com `%` no começo montado no código). O `FindAsync` que carrega a entidade antes de gravá-la, no mesmo método, não conta como leitura: o `UpdateAsync` do repositório aparece só como alteração.
- [ ] `Entry(x).State = Modified` como alteração; repositório que devolve a lista por uma variável local ou por outro método que a repassa; levantamento antigo com alvo "DbSet" só muda quando é refeito.

## Fase 43c: o que o reteste mostrou `[x]` (pedido do dono em 2026-11-06)

O reteste no mesmo sistema, com as Fases 43a e 43b, deu 14 achados verdadeiros e nenhum falso nos fluxos, mas mostrou cinco lacunas.

**Banco nos fluxos:**
- [x] N+1 e "ToList antes do filtro" através de um serviço fino: o laço (ou o filtro em memória) chama um método que só repassa a chamada a outro, até o repositório. Hoje só vale a chamada direta.
- [x] A chamada à interface que redefine com `new` um membro da interface base genérica (`ITarefaRepository : IRepository<Tarefa>`, com o `GetAllAsync` redefinido) ainda fica sem resolver no sistema real; reproduzir a forma exata e resolver.
- [x] O `LIKE` com curinga no começo montado no código não é achado quando a coluna tem índice trigrama no esquema.

**Análise de consulta e mapa:**
- [x] "Coluna sem índice" não acusa a coluna a mais do filtro quando um índice da mesma tabela já atende o filtro pelas colunas iniciais (o `archived_at` depois de `(tenant_id, space_id)`).
- [x] A exceção da lista filha usa outro critério: a chave do tenant é a coluna de chave estrangeira cujo nome aparece em pelo menos metade das tabelas do esquema, e não a tabela apontada por três ou mais.
- [x] `NOT (t.coluna_booleana)` e `t.coluna_booleana` contam como igualdade para o índice.
- [x] `Dictionary<string, string>` com `HasColumnType("jsonb")` e `HasConversion` é coluna do modelo.
- [x] Testes: fluxos `EXT-DBF-018` a `029` (fixture de dois projetos `Fixtures/TarefasMulti`), consulta `EXT-PGQ-031` a `034`, `EXT-DBQ-020` a `022` e `EXT-PG-044` e `045`. A análise de consulta entra pelo PR #69.
- [x] Causa da chamada sem resolver: a interface derivada não redeclarava o membro; o símbolo chamado era o da interface base, e o Rastro procurava quem implementa a base (o repositório genérico e o derivado). Agora os candidatos são os tipos que implementam o tipo do receptor. Também corrigido no caminho: o corpo de método de expressão (`=> _repo.X()`) ficava de fora da leitura das chamadas.
- [ ] A cadeia do N+1 segue qualquer chamada até 4 saltos: um método no meio que só consulta num ramo também vira achado. `NOT (col = 1)` no T-SQL não conta como igualdade.

## Fase 43d: o que o segundo reteste mostrou `[x]` (pedido do dono em 2026-11-06)

No segundo reteste, os 7 fluxos deram 21 achados, todos verdadeiros, e a análise de consulta caiu para 27 falsos. Ficaram estas lacunas.

**Banco nos fluxos:**
- [x] "ToList antes do filtro" na forma real da lista: a lista vem de um repositório pelo serviço, é guardada numa variável (às vezes atribuída em ramos diferentes) e o `Where` em memória vem depois, inclusive por reatribuição (`itens = itens.Where(...)`).
- [x] Falso: "ToList antes do filtro" quando a consulta do repositório já filtra e limita no banco (`Where` com `Take`, `First`, paginação). O filtro em memória sobre poucas linhas não é achado.
- [x] N+1 de escrita: `ExecuteUpdate`, `ExecuteDelete`, `ExecuteSql` e SQL cru dentro de laço (direto ou pela cadeia de chamadas) também viram achado.

**Análise de consulta e mapa:**
- [x] Lista filha sem chave estrangeira declarada: a coluna `x_id` com a tabela `x` (ou o plural) no esquema conta como chave estrangeira implícita, menos a de partição.
- [x] Coluna sem índice: um achado por tabela, citando todas as colunas do filtro, em vez de um por coluna.
- [x] Propriedade com `HasColumnType("jsonb")` cuja cadeia de configuração termina em `.Metadata.SetValueComparer(...)` é coluna do modelo.
- [x] Testes: fluxos `EXT-DBF-030` a `037`; consulta `EXT-PGQ-035` e `036`, `EXT-DBQ-023` e `024` e `EXT-PG-046`. Decisões: o N+1 de escrita usa o achado de consulta dentro de laço, com mensagem própria; a gravação rastreada (`Update`, `Remove` com `SaveChanges`) não conta; a chave estrangeira implícita não exige índice. Causa do jsonb: a leitura da cadeia parava no `.Metadata`.

## Depois da Fase 43 (2026-11-06)

- [x] Terceiro teste no sistema real, com as Fases 43a a 43d: os fluxos deram 25 achados, todos verdadeiros; a análise de consulta caiu para 21 falsos, todos leitura sem limite em tabela pequena (só o banco vivo diz o tamanho).
- [x] Release 0.7.0 (PR #74), com o passo de UX: barras e achados da consulta em 360 px corrigidos (PR #73). Prints em `docs/telas/fase-43/`.
- [ ] Conferir se algum achado verdadeiro da análise de consulta se perdeu com o achado único por tabela (o teste caiu de 39 para 19 verdadeiros, sem lista item a item para comparar).
- [ ] A lista pelo espaço (filtro por chave estrangeira de uma tabela grande) fica silenciada pela regra da lista filha, e `IS NOT NULL` não conta como filtro para a leitura sem limite.
- [x] Dois testes do backend falharam uma vez no CI e passaram na nova rodada (`PortProbeTests` e `LiveDatabaseEndpointTests`). Causa: não havia disputa de porta; o `config.json` era gravado sem ser de uma vez e o recarregamento em segundo plano lia o arquivo pela metade, esvaziando a lista de repositórios (agora grava num arquivo ao lado e troca), e o teste do sondador de porta esperava só os 2 segundos do produto por um servidor recém-aberto, na suíte em paralelo (agora o teste dá 30 segundos).

## Fase 44: pendências do banco `[x]` (pedido do dono em 2026-11-07)

As três pendências que ficaram depois da Fase 43 e da Fase 40b.

- [x] **Banco vivo pelo MCP.** Hoje o banco vivo é só pela tela e pela API, e a sugestão da consulta feita pelo agente vem sem o custo estimado de antes e depois. Com banco vivo cadastrado, o MCP passa a pedir o plano estimado (ferramenta nova) e o `propose_query_rewrite` devolve o custo das duas consultas, como a tela. A string de conexão continua sem sair do Rastro: o agente só vê o plano e o custo. Atualiza a ADR-006, o `SECURITY.md`, o README e as instruções do MCP.
- [x] **Conferir se algum achado verdadeiro se perdeu** com o achado único por tabela (de 39 para 19 verdadeiros no terceiro reteste). Gerar de novo as consultas do sistema real e rodar a análise de antes (`3db48f0^`) e a de agora, comparando item a item, só de leitura no repositório do dono. O que se perdeu vira correção nesta fase.
- [x] **Leitura sem limite pelo tamanho da tabela.** Com banco vivo, o catálogo dá a estimativa de linhas de cada tabela (sem ler dados): a leitura sem limite só é achado em tabela grande, e a exceção da lista filha deixa de valer quando a lista do pai é grande (a lista pelo espaço). Sem banco vivo, o comportamento de hoje continua. `IS NOT NULL` e `IS NULL` não contam como filtro que limita a leitura.
- [x] Reteste no sistema real e documentação (ADR-006, `SECURITY.md`, README, README do pacote, `visao.md`).
- **Registro (2026-11-07):**
  - **MCP:** a ferramenta `get_query_plan` e o custo de antes e depois no `propose_query_rewrite` reaproveitam o serviço da tela (mesma porta do leitor de SQL). O MCP lê as chaves da DataProtection do banco local só para decifrar (nunca cria nem grava chave), sem referenciar o `Rastro.Ai`.
  - **Tamanho da tabela:** a estimativa sai de `pg_class.reltuples` e de `sys.partitions` (sem permissão nova), com os limites de 1.000 e 100.000 linhas (`TableSize`), e aparece no painel da tabela.
  - **Comparação item a item no sistema real:** 52 consultas, na versão de antes do achado único por tabela e na de agora. O agrupamento não perdeu nada; o que se perdeu foi a lista do espaço, calada pela chave estrangeira implícita. Daí a regra da entidade principal: sem estimativa, a tabela apontada por duas ou mais outras não tem a exceção da lista filha.
  - **`lower()` sem achado:** a comparação mostrou que o `lower()` dentro de `SELECT EXISTS (...)` nunca era analisado; agora é, nos dois dialetos.
  - **Reteste com o código novo:** voltaram a lista do espaço, a de arquivadas (`IS NOT NULL`), os dois `lower()` e um `OR` que ficava escondido no mesmo `EXISTS`; os épicos e as features do espaço passaram a ter achado de gravidade baixa (a tabela de épicos é apontada por 11 tabelas). Subtarefas, comentários e status do espaço continuam sem achado.
  - **Testes:** backend 730/730, frontend 561/561.

## Campos escondidos no cartão do mapa do banco `[x]` (pedido do dono em 2026-11-07)

Tabela com mais de 12 colunas mostrava "+N campos (todos no painel)", um texto cinza que não parecia clicável. Clicar nele abria o painel, como clicar em qualquer ponto do cartão, e um segundo clique fechava o painel. O dono quer ver a tabela inteira no próprio mapa, sem ter que abrir um gerenciador de banco.

- [x] A linha "+N campos" vira um botão próprio: abre o cartão no mapa com todos os campos e, aberto, mostra "Mostrar menos". O cartão continua mostrando os 12 primeiros por padrão.
- [x] O desenho se refaz com a altura nova (os cartões de baixo descem), de forma determinística: o mesmo esquema com os mesmos cartões abertos dá o mesmo desenho. A ligação de uma chave estrangeira que estava escondida no "+N" passa a sair da linha dela.
- [x] Trocar de repositório fecha os cartões abertos. O PNG e o SVG exportados saem como o mapa está na tela.
- [x] Testes do desenho e da tela, com o clique de verdade no navegador e os prints em `docs/telas/`.
- **Registro (2026-11-07):** a linha virou um botão ao lado do cartão (o cartão já é um botão e não pode ter outro dentro), por cima da última linha dele. Conferido no navegador com clique de verdade, num esquema fictício de 18 campos: abre, fecha e não abre o painel. Prints em `docs/telas/mapa-banco-campos/`. Testes `WEB-DB-009` e `WEB-DBV-017`.

## Repaginação da usabilidade `[x]` (pedido do dono em 2026-11-06)

O dono não achou a tela do banco: a entrada era um link de texto na barra do levantamento, e o cabeçalho não tem navegação. Ele pediu para repaginar a usabilidade. O levantamento está em [`plano-ux-2026-11-06-repaginacao.md`](../frontend/plano-ux-2026-11-06-repaginacao.md): a Fase A (entradas e rótulos) e a Fase B (comportamento) seguem o ciclo normal; a Fase C (cabeçalho com as áreas do produto, tela inicial por repositório, barra do levantamento em grupos e textos) espera a aprovação do dono item a item, com um protótipo antes.

## Janela própria ao digitar `rastro` `[x]` (pedido do dono em 2026-11-06; depois da repaginação)

O dono quer que o `rastro` abra como um app, numa janela própria, sem passar pelo navegador. Proposta: Photino.NET, que abre uma janela nativa a partir do próprio executável .NET, com o WebView do sistema (WebView2 no Windows, WebKit no Mac e no Linux). O pacote cresce poucos MB e continua sendo `dotnet tool`. A API segue em `127.0.0.1:7450`, então a tela continua abrindo no navegador, como hoje.
- [x] ADR comparando o Photino, o navegador em modo app (`--app`) e o Electron (recusado: Chromium e Node no pacote, cerca de 150 MB por sistema): [ADR-007](../adr/adr-007-janela-propria.md). Photino.NET 4.0.16 com o Photino.Native 4.0.22, Apache-2.0; as bibliotecas nativas viajam em `runtimes/<rid>/native` dentro do pacote, sem passo novo no `empacotar.ps1`.
- [x] `rastro` abre a janela; `rastro --sem-janela` mantém o comportamento de hoje, para quem usa só o MCP ou o navegador. A janela só abre no pacote rodando de verdade (não nos testes, não com `--urls` ou `dotnet run`, não sem a tela embutida). Fechar a janela encerra o Rastro e Ctrl+C fecha a janela. Sem WebView2, sem `libwebkit2gtk-4.1` ou sem tela, o Rastro avisa no console e segue no navegador (conferido antes, porque o Photino tentaria baixar o WebView2 sozinho e o GTK sem tela derruba o processo). O `Program` ficou sem `await` antes da janela, para ela rodar na thread principal no macOS; no Windows, numa thread STA.
- [x] Botão "Procurar…" ao lado de cada campo de caminho (cadastro do repositório na tela inicial e nas Configurações), que abre a caixa de "Escolher pasta" do sistema pela janela própria e devolve o caminho completo; o campo continua aceitando o caminho digitado (pedido do dono em 2026-11-06). Só aparece com a ponte do Photino; mensagens com formato fixo (`{"type":0,"requestId":"pasta-N"}` e a resposta com o caminho absoluto ou `null`), conferidas dos dois lados. Testes: `API-JAN-001` a `011` e `UI-JAN-001` a `008`. Conferido no pacote instalado, no Windows: a janela abre, o "Procurar…" preenche o campo, o Chrome abre o mesmo endereço ao mesmo tempo e fechar a janela encerra o processo.
- [x] Documentação do ambiente do cliente no mesmo PR (README, README do pacote, `visao.md`, `SECURITY.md`, `CLAUDE.md`).
- [ ] "Procurar…" no navegador, por uma rota local da API com a proteção de origem, se o dono quiser. A janela no macOS e no Linux só foi decidida pela documentação do Photino; falta conferir numa máquina de cada.

## Levantamento de UX do fim do plano `[x]`

- [x] **Fim do plano do banco, 2026-11-06** (Fases 36 a 42): [`plano-ux-2026-11-06-fim-plano-banco.md`](../frontend/plano-ux-2026-11-06-fim-plano-banco.md), com 54 achados em cinco fases (PROPOSTA, nada implementado). As pendências do plano do banco estão consolidadas em [Fim do plano do banco](#fim-do-plano-do-banco-pendências-consolidadas).

- [x] **Revisão geral de 2026-10-30** (pedido do dono, depois da Fase 28): [`plano-ux-2026-10-30-revisao-geral.md`](../frontend/plano-ux-2026-10-30-revisao-geral.md). O botão desalinhado era o "Mostrar chamadas do .NET" (8px abaixo dos vizinhos); as três alturas de controle viraram dois tokens; o texto do passo, o rótulo da moldura na abertura e o desenho consolidado foram acertados. Os itens de risco alto (fase A) esperam aprovação.

- [x] Frontend inteiro levantado em 2026-10-16 (3 agentes de leitura, padrão `ux-refactor-process`): plano em [`docs/frontend/plano-ux-2026-10-16.md`](../frontend/plano-ux-2026-10-16.md), com 46 achados em 4 fases. As Fases A a C (risco baixo e médio) podem ser executadas; a Fase D (texto, esconder, reorganizar) espera aprovação do dono item a item.

## Fora de escopo deste plano

- SaaS (o código do cliente nunca sai da máquina dele; ver ADR-002).
- Linguagens além de C# e TypeScript (Angular): a [ADR-005](../adr/adr-005-extrator-angular.md) abriu só o Angular.
- Frontends que não são Angular (React, Vue) e o template do Angular além do leitor leve da Fase 26 (`@switch`, `ng-template`, diretivas próprias).
- Chamada para SaaS de terceiros (gateway, CRM, e-mail): continua `External`, porque o código deles não está na máquina.
- Mensageria (fila e tópico) como fronteira entre sistemas: mesmo mecanismo da costura HTTP, com um índice de consumidores. Entra depois que a Fase 25 provar o modelo de trechos.
- Edição, prompt e validação através de vários sistemas (a Fase 25 trava os três num trecho só).

## Ordem e justificativa

0 → 1 → 2 → 3 é o caminho mínimo para saber se o projeto vale a pena (checkpoint go/no-go na Fase 3). O modo agente (4) vem antes do modo provedor (5) porque já entrega IA sem custo de API e cria o validador de prova que os dois usam. IA (4–6) vem antes da edição (7–9), porque editar um grafo com buracos gera prompts errados. As Fases 10 e 11 são independentes e podem trocar de ordem conforme a necessidade.
