# Rastro AI

Segue o rastro de uma funcionalidade no seu código .NET, da entrada (controller, job, handler) até o fim, com cada decisão, cada laço e a implementação real atrás de cada interface do DI. Mostra o fluxo como um grafo que se lê de relance e gera o prompt exato para a IA alterar aquele ponto.

O Rastro roda só na sua máquina: o código analisado nunca sai dela. Funciona sem IA; com IA, ela só propõe, e quem confirma é você.

- **Linha do tempo do método:** os passos na ordem do código, numerados (1, 2, 2.1), com o que cada método chama saindo como galho. Três desenhos (horizontal, vertical e árvore), com a mesma regra.
- **Para quem não programa também:** os comandos aparecem em português ("Se ...", "Para cada ... em ..."), com o nome legível do método e uma etiqueta do que o passo faz no mundo (banco, mensagem, cache, arquivo, fila). Um resumo em frase abre a visão geral.
- **Prova do compilador:** cada ligação mostra a origem (extraída do código, inferida pela IA ou confirmada por você). O que a IA propõe passa pelo Roslyn antes de entrar.
- **Mudança desenhada no grafo:** você edita o fluxo, o Rastro gera o prompt da mudança e, depois que o agente aplica, valida contra o código novo (build, testes e o fluxo de novo).
- **Arquitetura:** exporta o fluxo em Mermaid e SVG e a arquitetura em C4 (Structurizr), e aponta melhorias e dívida técnica (DDD, código limpo, métricas).

![Linha do tempo de um fluxo, com os passos numerados e o resumo na visão geral](docs/telas/fase-24/01-linha-do-tempo.jpg)

## Instalar e usar

Requer o SDK do .NET 10 (o Rastro carrega a sua solution com o MSBuild dele).

```bash
dotnet tool install -g rastro-ai
rastro                      # tela em http://127.0.0.1:7450 (só nesta máquina; --porta troca)
```

Cadastre os repositórios em Configurações (a engrenagem no alto da tela) ou pelo terminal. Os comandos de build e de testes, usados na validação de uma mudança, só se informam pelo terminal:

```bash
rastro repo add minha-api C:\codigo\minha-api --build "dotnet build MinhaApi.slnx" --testes "dotnet test MinhaApi.slnx --no-build"
```

| Comando | O que faz |
|---|---|
| `rastro` | Sobe a tela (API e front embutidos), só em `127.0.0.1` |
| `rastro repo add/remove/list` | Repositórios que o Rastro pode ler (`~/.rastro/config.json`) |
| `rastro mcp` | Servidor MCP para o seu agente de IA |

Os dados ficam em `~/.rastro` (banco SQLite `rastro.db` e `config.json`); a variável `RASTRO_HOME` troca a pasta.

## IA opcional e plugável

O Rastro **funciona sem IA**. A IA entra por dois modos, e os dois passam pela mesma porta, o validador de prova do núcleo ([ADR-001](docs/adr/adr-001-ia-opcional-e-plugavel.md)):

| Jeito de usar | Precisa de quê | Quem completa o que o compilador não resolveu |
|---|---|---|
| **Sem IA** | nada | você, à mão |
| **Modo agente** (`rastro mcp`) | um agente com MCP (ex.: Claude Code) | o agente, usando as ferramentas do Rastro |
| **Modo provedor** | chave de API de Claude, OpenAI ou Gemini, em Configurações | o próprio Rastro |

```
             Rastro.Extractor (Roslyn) ──► Rastro.Core (grafo + validador de prova)
                                                    ▲              ▲
                                         propostas  │              │  propostas
                                       Rastro.Mcp (agente)   Rastro.Ai (provedor)
```

Regra que não se quebra: `Rastro.Core` e `Rastro.Extractor` **nunca** referenciam código de IA, e um teste de arquitetura garante isso.

### Com o Claude Code (MCP)

```bash
claude mcp add rastro -- rastro mcp
```

Ferramentas: `list_repositories`, `analyze_flow`, `list_surveys`, `get_flow`, `list_unresolved`, `get_node_code`, `search_methods`, `propose_edge`, `describe_node`, `impact_of`, `get_change_prompt`, `check_change`, `list_findings`, `get_refactor_prompt` e `get_c4`. Peça, por exemplo: *"use o rastro para completar os buracos do levantamento X e escrever as regras das decisões"*.

- Toda proposta passa pelo validador de prova: o compilador confere o método de destino e o `arquivo:linha`. Prova errada volta recusada, com o motivo.
- Proposta aceita entra como **inferida**. **Só você confirma ou descarta**, na tela; o agente não tem ferramenta para isso.

## Como funciona

- **Extrator (Roslyn e MSBuild):** carrega a solution real, com os tipos resolvidos, segue as chamadas a partir do ponto de entrada e resolve as interfaces pelo registro do DI. O que o compilador não alcança (reflection, despacho dinâmico) vira um passo "não resolvido", com o motivo.
- **Grafo como visão:** o código é a fonte da verdade. Cada levantamento guarda o hash do commit, e o grafo é refeito a cada análise.
- **Desenho próprio:** a linha do tempo é calculada pelo Rastro (sem ELK), na ordem do código, e desenhada com o Foblex Flow.
- **Segurança:** a API só escuta em `127.0.0.1`, recusa pedido de outro site e só lê pastas cadastradas. Build e testes da validação rodam sem shell, com o comando da configuração.

Visão e decisões em [`docs/arquitetura/visao.md`](docs/arquitetura/visao.md) e nas [ADRs](docs/adr/).

## Estrutura

| Pasta | O quê |
|---|---|
| [`backend/`](backend/) | .NET 10: extrator (Roslyn), núcleo, API, MCP, IA (provedores) e testes (xUnit) |
| [`frontend/`](frontend/) | a tela em Angular 20 e Foblex Flow, com testes no Karma |
| [`docker/`](docker/) | imagem opcional, para rodar o Rastro num container |
| [`docs/`](docs/) | visão, ADRs, plano faseado e prints das telas |
| [`scripts/`](scripts/) | geração do pacote e conferência do que vai a público |

## Desenvolver

| Como | Comando | Endereço |
|---|---|---|
| API local | `dotnet run --project backend/src/Rastro.Api` | http://localhost:5310 |
| Front local | `cd frontend && npm install && npm start` (proxy de `/api` para a API local) | http://localhost:4300 |
| MCP local | `dotnet run --project backend/src/Rastro.Api -- mcp` | stdio |
| CLI de análise | `dotnet run --project backend/src/Rastro.Cli -- analyze <solution> <Tipo.Metodo> --out fluxo.json` | |

Antes de abrir um pull request:

```bash
dotnet build backend/Rastro.slnx && dotnet test backend/Rastro.slnx
cd frontend && npm run build && npm run test:ci
```

Pacote local: `powershell -File scripts/empacotar.ps1` gera `artifacts/rastro-ai.<versão>.nupkg` (front embutido no `wwwroot` da API), e `dotnet tool install -g rastro-ai --add-source artifacts` instala para testar.

**Docker (opcional):** `docker compose -f docker/docker-compose.yml up -d --build` sobe em http://localhost:7400, com o banco no volume `rastro-data` e os repositórios montados só para leitura (`REPOS_DIR`, `REPO_0_NAME` e `REPO_0_FOLDER`).

## Como foi construído

Construído em par com o Claude (Claude Code), fase a fase, com plano, testes e prints de cada tela a cada fase: [`docs/arquitetura/plano-mvp.md`](docs/arquitetura/plano-mvp.md).

## Licença

[MIT](LICENSE). Falhas de segurança: veja [SECURITY.md](SECURITY.md).
