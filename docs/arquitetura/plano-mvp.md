# Plano do Rastro AI (MVP): EXECUTADO (Fases 0 a 14); Fases 15 e 16 esperam o uso real

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

## Fase 24: código aberto no GitHub `[ ]` (pedido do dono em 2026-10-26)

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
- [ ] **8. Card no portfólio:** no `meu-portfolio` (`projects.ts`), com problema, solução e resultado, tecnologias (.NET 10, Roslyn, MSBuild, Angular 20, Foblex Flow, SQLite, MCP), imagem do grafo, `githubUrl` e o link do NuGet no texto, sem `demoUrl`. Segue as regras de commit e release daquele repositório.

## Fase 22: modelos de ramificação `[ ]` (ideia do dono em 2026-10-17, a detalhar)

Próximo passo da Fase 21: o usuário escolhe como quer ver a ramificação, entre modelos prontos. Os modelos ainda serão definidos com o dono.

## Fase 15: página do projeto e identidade visual `[ ]` (revista pela ADR-004: o Rastro é aberto e gratuito, sem venda)

- [ ] Identidade visual do produto (cor e tema), escolhida pelo dono a partir de propostas aplicadas na tela.
- [ ] Landing page em português, inglês e espanhol.
- [-] ~~Preço único e baixo no começo; pagamento por serviço pronto (Lemon Squeezy, Paddle ou similar) que emite a licença sozinho e manda por e-mail.~~ Cancelado pela ADR-004.
- [ ] Textos do produto (tela, pacote, mensagens da CLI) nos três idiomas.

## Fase 16: plano enterprise `[ ]` (futuro)

- [ ] IA com os tokens contratados pela empresa: endpoint próprio (Azure OpenAI, Bedrock ou gateway interno) no modo provedor.
- [-] ~~Licença por assentos e emissão em lote.~~ Cancelado pela ADR-004.

## Levantamento de UX do fim do plano `[x]`

- [x] Frontend inteiro levantado em 2026-10-16 (3 agentes de leitura, padrão `ux-refactor-process`): plano em [`docs/frontend/plano-ux-2026-10-16.md`](../frontend/plano-ux-2026-10-16.md), com 46 achados em 4 fases. As Fases A a C (risco baixo e médio) podem ser executadas; a Fase D (texto, esconder, reorganizar) espera aprovação do dono item a item.

## Fora de escopo deste plano

- SaaS (o código do cliente nunca sai da máquina dele; ver ADR-002).
- Linguagens além de C#.
- Leitura do frontend do repositório-alvo (fluxos Angular).

## Ordem e justificativa

0 → 1 → 2 → 3 é o caminho mínimo para saber se o projeto vale a pena (checkpoint go/no-go na Fase 3). O modo agente (4) vem antes do modo provedor (5) porque já entrega IA sem custo de API e cria o validador de prova que os dois usam. IA (4–6) vem antes da edição (7–9), porque editar um grafo com buracos gera prompts errados. As Fases 10 e 11 são independentes e podem trocar de ordem conforme a necessidade.
