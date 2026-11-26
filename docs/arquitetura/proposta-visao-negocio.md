# Rastro: visão de negócio e quadro de fluxos

Especificação para implementar no Rastro AI uma **visão de negócio**: um desenho no estilo do draw.io, com poucos passos em linguagem de negócio, vários fluxos lado a lado e cada balão ligado à linha de código de onde veio.

> Este documento não usa código nem dado de cliente. Os exemplos usam os sistemas fictícios `loja-web` (Angular), `api-pedidos` e `api-pagamentos` (.NET), os mesmos da proposta de fluxo entre sistemas. Para validar, monte esses três repositórios como fixture de teste.

## 0. Decisões que ajustam esta proposta (2026-11-25)

Registradas na [ADR-010](../adr/adr-010-visao-de-negocio.md); o plano está nas Fases 54 a 61 do [`plano-mvp.md`](plano-mvp.md). Quando esta seção e o resto do texto divergem, vale esta seção.

| Tema | Decisão |
|---|---|
| Exportar para draw.io | Entra primeiro (Fase 54) para o grafo e o Mapa por sistemas que já existem; a visão de negócio e o quadro usam o mesmo gerador depois. |
| Biblioteca da tela (10.2) | Foblex Flow, que o Rastro já usa, com layout automático próprio em camadas. O elkjs fica como plano B. |
| Onde nasce o arquivo (12 e 14) | Na tela, como o Mermaid e o SVG da Fase 12. Sai a rota `GET api/boards/{id}/export`. |
| Cores (10.4) | Tokens por paleta, com par claro e escuro; o verde do Início vale. O `.drawio` usa as cores padrão do draw.io. |
| Tipos de nó (4.1) | O `NodeKind` real é `Entry`, `Method`, `Decision`, `Loop`, `Try`, `Return`, `Throw`, `External`, `Unresolved` e `Jump`. Não há `Database`: a escrita no banco vem de `FlowNode.Db` (`DbOperation` `Insert`, `Update`, `Delete` e `Execute`). |
| Layout do levantamento sozinho | O botão "Visão de negócio" do levantamento guarda a posição num escopo próprio do levantamento; o quadro guarda o seu. |
| Protótipo | O protótipo navegável citado na proposta original usa dado de cliente e não entra no repositório. |

## 1. Problema

O Rastro já segue um fluxo do clique na tela até a última chamada externa. O resultado serve a quem programa, mas não a quem precisa entender a regra de negócio:

- O grafo tem todos os passos do código. Um fluxo real de cancelamento chega a 165 passos.
- Os rótulos são nomes de método e condições em código, como `Se this.isGarantiaWithSpecialEndorsement()`.
- Cada levantamento fica numa tela. Um processo de negócio costuma ter vários pontos de entrada (a tela do cliente, um job diário, uma integração), e não há como ver todos juntos.
- A visão consolidada é um Mermaid gerado: dá para editar texto e cor à mão, passo a passo, mas a tela não se comporta como uma tela de desenho (arrastar a tela, mover balões, zoom no ponteiro).
- A referência ao código só aparece no painel, depois do clique.

Hoje isso se resolve desenhando à mão no draw.io e copiando arquivo e linha de cada balão. O desenho fica bom, mas desatualiza no primeiro commit e não tem ligação com o levantamento.

## 2. Objetivo

Gerar, a partir de um ou mais levantamentos, um **quadro** com a visão de negócio de cada fluxo:

1. Só os **passos-chave**: início, decisões que mudam o resultado, notificações, recusas, chamadas entre sistemas e fim.
2. **Tipo de negócio** por passo, com cor própria: Início, Decisão, Notificação, Recusado ou descartado, Chamada entre sistemas, Passo, Fim.
3. **Texto de negócio** em cada balão, proposto por regra, pela IA ou pelo agente via MCP, e confirmado pelo usuário.
4. **Referência no balão**: `repositório · arquivo:linha`, com link para o host do repositório (Azure DevOps, GitHub, GitLab, Bitbucket) e para o editor.
5. **Tela livre**: arrastar a tela, mover balões, zoom com Ctrl + roda, posição salva.
6. **Exportar** para `.drawio`, Mermaid, SVG e PNG.

## 3. Fora de escopo

- Editar o código a partir da visão de negócio. A edição continua no grafo, como hoje.
- Desenho livre de formas que não vêm do levantamento (setas soltas, caixas de texto avulsas). Só o glossário e a legenda, que são do quadro.
- Colaboração em tempo real. O quadro é local, como o resto do Rastro.
- Mensageria como fronteira entre sistemas. Segue o plano da proposta de fluxo entre sistemas.

## 4. O que já existe no Rastro 0.10.0

### 4.1 Modelo do grafo

`FlowGraph`, gravado em `surveys.graph_json`:

| Campo | Conteúdo |
|---|---|
| `schemaVersion`, `entryPoint`, `entryNodeId` | Versão do formato, ponto de entrada e nó inicial |
| `nodes[]` | `id`, `kind` (`NodeKind`), `label`, `symbol`, `location` (`file`, `line`), `project`, `system`, `condition`, `detail`, `reason`, `db` |
| `edges[]` | `from`, `to`, `label`, `order`, `confidence` (`EdgeConfidence`), `http`, `location` |

Os valores de `NodeKind` estão na seção 0.

### 4.2 Tabelas que interessam

| Tabela | Uso hoje | Uso na visão de negócio |
|---|---|---|
| `surveys` | Levantamento e grafo | Fonte dos passos |
| `survey_segments` | Trechos de cada sistema no fluxo entre sistemas | Faixa por sistema e repositório de cada passo |
| `survey_diagrams` | Mermaid editado da visão consolidada | Nenhum: a visão de negócio não usa Mermaid |
| `survey_proposals` | Propostas da IA e do agente, com `origin`, `status`, `evidence_file`, `evidence_line` | Propostas de texto de negócio e de tipo |
| `node_notes` | Notas por nó | Nota aparece no painel do passo |
| `ai_rule_cache` | Texto de regra por hash do código | Mesmo mecanismo para o texto de negócio |
| `route_indexes` | Índice de rotas por commit | Detecta chamada entre sistemas |

### 4.3 Outras peças

- `FlowMap` (lanes, blocos, links, `MapLaneKind`, `MapBlockKind`, `MapLinkKind`) e `FlowRule`: base do "Mapa por sistemas" e das regras. A derivação de passos-chave pode reaproveitar a identificação de "decisões que mudam o resultado" que o mapa já faz.
- MCP: `get_flow`, `get_flow_map`, `get_flow_rules`, `describe_node`, `get_node_code`, `list_cross_edges`, `propose_edge`, `list_unresolved`, entre outras.
- Regra de arquitetura: `Rastro.Core` e `Rastro.Extractor` não referenciam IA. A derivação de passos-chave fica no `Core`, sem IA.

## 5. Conceitos novos

| Conceito | Definição |
|---|---|
| **Quadro** (`Board`) | Conjunto ordenado de levantamentos desenhados lado a lado, cada um numa faixa, com glossário e legenda |
| **Faixa** (`BoardLane`) | Um levantamento dentro do quadro: título de negócio, ponto de entrada, repositórios |
| **Visão de negócio** (`BusinessFlow`) | Grafo reduzido de um levantamento: só passos-chave e as ligações entre eles |
| **Passo-chave** (`BusinessStep`) | Nó da visão de negócio. Aponta para um ou mais nós do grafo original |
| **Tipo de negócio** (`BusinessKind`) | `Start`, `Decision`, `Notification`, `Rejection`, `CrossSystem`, `Step`, `End` |
| **Texto de negócio** | Rótulo do passo em linguagem de negócio. Tem origem (`Rule`, `Ai`, `Agent`, `User`) e estado (`Proposed`, `Confirmed`) |
| **Referência de código** | Repositório, arquivo, linha, commit e branch do nó que representa o passo |
| **Layout** | Posição e tamanho de cada passo e pontos de dobra das ligações, por quadro |

## 6. Derivação dos passos-chave

Função pura em `Rastro.Core`: `BusinessFlow Derive(FlowGraph graph, IReadOnlyList<SurveySegment> segments, BusinessViewOptions options)`.

### 6.1 Regras de seleção

Percorra o grafo a partir de `entryNodeId`, em ordem (`edge.order`), e marque como passo-chave:

| Regra | Condição | Tipo |
|---|---|---|
| R1 | O nó de entrada | `Start` |
| R2 | `Decision` com pelo menos dois ramos que levam a passos-chave diferentes ou a resultados diferentes | `Decision` |
| R3 | Chamada que casa com um padrão de notificação (ver 6.2) | `Notification` |
| R4 | `Return` ou `Throw` que encerra com erro (ver 6.3) | `Rejection` |
| R5 | Aresta com `http` resolvida para outro repositório, ou `External` de HTTP | `CrossSystem` |
| R6 | Escrita no banco (`Db` com insert, update, delete ou execute) ou chamada externa de escrita (`Post`, `Put`, `Patch`, `Delete`) | `Step` |
| R7 | Último nó de cada caminho que termina sem erro | `End` |

Uma `Decision` que não muda o resultado (os dois ramos chegam ao mesmo próximo passo-chave sem passar por outro) **não** entra: ela se funde ao passo de cima.

Os nós que não viram passo-chave são agrupados no passo-chave anterior do mesmo caminho, e a lista fica em `BusinessStep.SourceNodeIds`. O painel mostra essa lista recolhida ("12 passos de código neste bloco").

### 6.2 Notificação

Configurável em `config.json`, com padrões padrão que não citam nenhum produto ou cliente:

```json
{
  "Rastro": {
    "BusinessView": {
      "NotificationPatterns": [
        "*Email*Service.Send*",
        "*Mail*.Send*",
        "*Notification*.Register*",
        "*Notification*.Send*",
        "*Notifier*.Notify*",
        "*SmtpClient.Send*",
        "*IHubContext*.Send*"
      ],
      "RejectionReturnTypes": ["BadRequest", "UnprocessableEntity", "NotFound", "Forbid", "Conflict", "Problem"],
      "MaxStepsPerFlow": 25
    }
  }
}
```

O padrão casa com `symbol` do nó (`Tipo.Método`). Quando o símbolo é uma interface, o Rastro já resolve a implementação pela injeção de dependência: o padrão vale para os dois nomes.

O texto padrão da notificação diz o canal quando o símbolo deixa claro: e-mail (`Email`, `Mail`, `Smtp`), aviso no app (`Notification`, `Hub`). Quem recebe vem do argumento da chamada, quando o extrator consegue ler (`detail`); senão, fica para a IA ou o usuário.

### 6.3 Recusa

- `Return` de um tipo de resultado de erro (`RejectionReturnTypes`), de `StatusCode` 4xx, ou de um `ResponseViewModel`/`Result` com status de erro, quando o extrator identifica a constante.
- `Throw` de exceção que não é capturada no próprio fluxo.
- No Angular, `return` dentro de um guarda (`if (...) return;`) antes da chamada HTTP, e `[disabled]` do template: ambos viram `Rejection` com o texto "Bloqueado na tela".

### 6.4 Ligações

Cada ligação da visão de negócio liga dois passos-chave consecutivos de um mesmo caminho. O rótulo vem do ramo da decisão de origem (`edge.label` do grafo: "Sim", "Não", "Se sim", o `case` do `switch`). A confiança é a menor entre as arestas do caminho agrupado: se alguma era inferida, a ligação aparece tracejada.

### 6.5 Limite

Se a derivação passar de `MaxStepsPerFlow`, as decisões mais profundas (maior distância da entrada) que não levam a `Notification` nem a `Rejection` são fundidas primeiro, até caber. A tela avisa: "Fluxo resumido em N passos; X decisões foram agrupadas".

### 6.6 Pseudocódigo

```text
Derive(graph):
  key = {}                       // nodeId -> BusinessKind
  key[entry] = Start
  for node in graph.nodes:
    if node.kind == Decision and ChangesOutcome(node): key[node] = Decision
    elif MatchesNotification(node.symbol):         key[node] = Notification
    elif IsRejection(node):                         key[node] = Rejection
    elif IsCrossSystem(node, outgoingEdges(node)):  key[node] = CrossSystem
    elif IsWrite(node):                             key[node] = Step
  for path in AllPaths(entry):                      // DFS com corte de ciclo
    last = path.last
    if last not in key and not IsRejection(last): key[last] = End
  steps = key.map(n -> BusinessStep(n, kind, sources = Collapse(n)))
  edges = for each key node k, for each path to next key node k2: BusinessEdge(k, k2, label = firstBranchLabel(path))
  return Trim(steps, edges, options.MaxStepsPerFlow)

ChangesOutcome(d):
  targets = { NextKey(branch) for branch in d.branches }
  return targets.size >= 2
```

`NextKey` segue o ramo até o primeiro nó que seria passo-chave sem contar a própria decisão. Para não entrar em ciclo, derive primeiro os tipos R3 a R7 e só depois aplique R2.

### 6.7 Exemplo esperado (fixture)

Levantamento `loja-web · DetalhePedidoComponent.cancelar`, seguindo para `api-pedidos` e `api-pagamentos`:

| # | Tipo | Texto padrão (regra) | Referência |
|---|---|---|---|
| 1 | Start | Clique em Cancelar no detalhe do pedido | `loja-web · detalhe-pedido.component.ts:L` |
| 2 | Decision | Pedido já enviado? | `loja-web · detalhe-pedido.component.ts:L` |
| 2.1 | Rejection | Bloqueado na tela | `loja-web · detalhe-pedido.component.html:L` |
| 3 | CrossSystem | POST api/pedidos/{id}/cancelamento | `loja-web · pedidos.service.ts:L` |
| 4 | Decision | Dentro do prazo de cancelamento? | `api-pedidos · CancelamentoService.cs:L` |
| 4.1 | Rejection | Recusa: fora do prazo | `api-pedidos · PedidosController.cs:L` |
| 5 | CrossSystem | POST api/estornos | `api-pedidos · PagamentosClient.cs:L` |
| 6 | Decision | Forma de pagamento é cartão? | `api-pagamentos · EstornosService.cs:L` |
| 7 | Notification | E-mail de confirmação do estorno | `api-pagamentos · EstornosService.cs:L` |
| 8 | End | Pedido cancelado | `api-pedidos · PedidosController.cs:L` |

O teste compara tipos, ordem e ligações. As linhas (`L`) vêm da fixture.

## 7. Texto de negócio

### 7.1 Origem

1. **Regra** (sem IA): monta o texto a partir de `symbol` humanizado (já existe Humanizer no pacote), `condition` e `detail`. Exemplos: `IsWithinCancellationWindow` → "Dentro do prazo de cancelamento?"; `SendAsync` de `IEmailService` → "Envia e-mail". Serve de ponto de partida e nunca é marcado como confirmado.
2. **IA** (com o uso "Sugestões" ligado): recebe o trecho de código do passo (até N linhas em volta da `location`), o tipo e o caminho até ele; devolve um texto curto. Fica como proposta, origem `Ai`.
3. **Agente via MCP**: ferramenta `propose_business_label`. Origem `Agent`.
4. **Usuário**: edita no painel. Origem `User`, já confirmado.

A tela mostra o texto proposto em itálico com o selo "proposto" até o usuário confirmar, como já acontece com as arestas inferidas.

### 7.2 Cache e desatualização

O texto confirmado fica ligado ao **hash do código** do passo (mesma ideia de `ai_rule_cache`). Se o código mudar num novo levantamento, o texto continua, mas ganha o selo "o código mudou desde a confirmação".

### 7.3 Regras do texto

- No máximo 120 caracteres. Decisão termina com "?".
- Sem nome de método, de classe ou de variável.
- A IA recebe a instrução de usar os termos do glossário do quadro, quando existir.

## 8. Referência de código e links

### 8.1 Texto no balão

`{repositório} · {arquivo}:{linha}`, só o nome do arquivo, em fonte monoespaçada de 9 a 10 px, abaixo do texto de negócio. O caminho completo fica no painel.

### 8.2 Link para o host

Montado a partir de `git remote get-url origin` do repositório cadastrado. Use o **commit** do levantamento (link estável); ofereça a branch como alternativa no painel.

| Host | Formato |
|---|---|
| Azure DevOps | `https://dev.azure.com/{org}/{projeto}/_git/{repo}?path=/{arquivo}&version=GC{commit}&line={L}&lineEnd={L}&lineStartColumn=1&lineEndColumn=200&lineStyle=plain&_a=contents` (branch: `version=GB{branch}`) |
| GitHub | `https://github.com/{owner}/{repo}/blob/{commit}/{arquivo}#L{L}` |
| GitLab | `https://gitlab.com/{grupo}/{repo}/-/blob/{commit}/{arquivo}#L{L}` |
| Bitbucket | `https://bitbucket.org/{workspace}/{repo}/src/{commit}/{arquivo}#lines-{L}` |

Normalize o remote antes: tire usuário (`https://usuario@dev.azure.com/...`), troque `git@host:` por `https://host/`, tire `.git` do fim. O formato SSH do Azure DevOps (`git@ssh.dev.azure.com:v3/{org}/{projeto}/{repo}`) precisa de conversão própria. Remote desconhecido: sem link para o host, só o do editor.

O arquivo vai com `encodeURIComponent` em cada segmento (Azure DevOps aceita o caminho inteiro codificado no `path`).

### 8.3 Link para o editor

- VS Code: `vscode://file/{caminho absoluto}:{L}`.
- Visual Studio e Rider: configuração em `config.json` (`Rastro:Editor:UrlTemplate`), com `{path}` e `{line}`.

Na janela Photino, links externos abrem no navegador padrão.

## 9. Quadro

### 9.1 Criação

- Na lista de levantamentos, selecionar dois ou mais e "Criar quadro"; ou, dentro de um levantamento, "Adicionar a um quadro".
- Nome do quadro (ex.: "Cancelamento de pedido") e título de negócio de cada faixa (ex.: "1. Cliente cancela pela tela", "2. Job noturno cancela pedidos vencidos").
- Ordem das faixas arrastável.

### 9.2 Conteúdo

- Uma faixa por levantamento, lado a lado, com título, ponto de entrada (`Tipo.Método`) e os repositórios do levantamento.
- **Glossário** do quadro: termos e definições, em texto simples. Usado pela IA no texto de negócio.
- **Legenda** das cores, gerada.

### 9.3 Desatualização

Reaproveita o "O código mudou" que o levantamento já mostra. No quadro, a faixa desatualizada ganha o aviso e o botão "Levantar de novo". Ao refazer o levantamento, a derivação roda de novo e o layout tenta manter cada passo no lugar, casando pelo `symbol` e pela `location`. Passo novo entra na posição automática; passo que sumiu sai e é listado num aviso ("3 passos saíram desde a última versão").

## 10. Tela

### 10.1 Onde fica

- Novo botão **Visão de negócio** no seletor de desenho do levantamento (ao lado de Grafo, Mapa por sistemas e Visão consolidada), mostrando só aquele fluxo.
- Nova rota `/boards/{id}` para o quadro, e item "Quadros" na barra de cima.

### 10.2 Desenho

Sem Mermaid. A escolha da biblioteca está na seção 0 e na ADR-010.

### 10.3 Interação

| Ação | Comportamento |
|---|---|
| Arrastar em área vazia | Move a tela (pan). Cursor `grab`/`grabbing` |
| Arrastar um balão | Move o balão, com encaixe na grade de 10 px. As ligações dele se redesenham |
| Clique num balão (sem arrastar, deslocamento < 4 px) | Seleciona e abre o painel |
| Ctrl/Cmd + roda | Zoom em volta do ponteiro, de 25% a 140% |
| Roda sem tecla | Rola a tela |
| Botões + / − / Ajustar | Zoom e enquadrar o quadro inteiro |
| Barra de espaço + arrastar | Pan, mesmo em cima de balão |
| Teclado | Tab percorre os balões na ordem do fluxo; Enter abre o painel; setas movem o balão selecionado 10 px; Esc limpa a seleção |
| Duplo clique no texto | Edita o texto de negócio no próprio balão |
| "Reorganizar" | Descarta o layout salvo da faixa e roda o layout automático de novo |
| Arrastar ponto de dobra | Ajusta o caminho da ligação; guarda os pontos no layout |

Toque: um dedo move a tela, pinça faz zoom, toque curto seleciona.

### 10.4 Balões

| Tipo | Forma | Cor (claro / escuro) |
|---|---|---|
| Start | Pílula | `#d5e8d4` borda `#82b366` / `#243a2a` borda `#6fa35c` |
| Decision | Losango | `#fff2cc` borda `#d6b656` / `#3b3420` borda `#c9a642` |
| Notification | Retângulo arredondado, texto em negrito | `#ffe6cc` borda `#d79b00` / `#40301c` borda `#e0a13a` |
| Rejection | Retângulo arredondado | `#f8cecc` borda `#b85450` / `#42262a` borda `#d0706a` |
| CrossSystem | Retângulo tracejado | `#eeeeee` borda `#9a9a9a` / `#2a2f2e` borda `#6f7874` |
| Step | Retângulo arredondado | `#ffffff` borda `#666666` / `#262c2b` borda `#6f7874` |
| End | Pílula | `#dae8fc` borda `#6c8ebf` / `#1f2d42` borda `#7ea2d6` |

São as mesmas cores do draw.io, para o export sair igual. Tudo em variáveis CSS, com tema claro e escuro.

Conteúdo do balão: texto de negócio (12 px), referência (`repositório · arquivo:linha`, 9 a 10 px, monoespaçada, cor secundária). Selo "proposto" quando o texto não foi confirmado; selo "inferido" quando o passo veio de ligação inferida.

Ligações: rótulo ("Sim", "Não") com fundo da tela por trás, para não cruzar a linha. Inferida: tracejada.

### 10.5 Painel do passo

1. Tipo (pílula com a cor) e texto de negócio, editável, com origem e estado ("Proposto pela IA", "Confirmado por você").
2. Código: repositório, arquivo completo, linha, commit, branch; botões "Abrir no {host}" e "Abrir no editor"; trecho do código (reaproveita `get_node_code`).
3. Caminho da entrada até aqui, em texto de negócio, com o rótulo de cada ramo.
4. Passos de código agrupados neste bloco (recolhido), cada um com o link para o nó no grafo.
5. Nota do passo (`node_notes`).
6. Botão "Ver no grafo": abre o grafo com o nó selecionado.

### 10.6 Estado vazio e carregamento

- Levantamento ainda rodando: faixa com o aviso "Levantando…" e a barra de progresso que já existe.
- Derivação sem passo-chave além da entrada: "Nenhuma decisão ou notificação encontrada neste fluxo. Veja o grafo completo."

## 11. Modelo de dados

### 11.1 Core (`Rastro.Core`)

```csharp
public enum BusinessKind { Start, Decision, Notification, Rejection, CrossSystem, Step, End }
public enum LabelOrigin { Rule, Ai, Agent, User }
public enum LabelState { Proposed, Confirmed }

public sealed record CodeRef(string Repository, string File, int Line, string CommitHash, string? Branch);

public sealed record BusinessStep(
    string Id,                       // estável: hash de (surveyId, nodeId)
    string NodeId,                   // nó do FlowGraph que representa o passo
    BusinessKind Kind,
    string Label,
    LabelOrigin LabelOrigin,
    LabelState LabelState,
    CodeRef? Ref,
    IReadOnlyList<string> SourceNodeIds,
    bool Inferred);

public sealed record BusinessEdge(string From, string To, string? Label, bool Inferred);

public sealed record BusinessFlow(
    Guid SurveyId, int SchemaVersion,
    IReadOnlyList<BusinessStep> Steps,
    IReadOnlyList<BusinessEdge> Edges,
    int CollapsedDecisions);

public sealed record Board(Guid Id, string Name, string? Glossary, IReadOnlyList<BoardLane> Lanes);
public sealed record BoardLane(Guid SurveyId, int Order, string Title);

public sealed record StepLayout(string StepId, double X, double Y, double W, double H);
public sealed record EdgeLayout(string From, string To, IReadOnlyList<(double X, double Y)> Points);
```

Os enums levam valor explícito (`= 0`, `= 1`) e viajam como `int` no JSON, pela regra do `CLAUDE.md`.

### 11.2 Tabelas novas (SQLite, migration do EF Core)

| Tabela | Colunas |
|---|---|
| `boards` | `id`, `name`, `glossary`, `created_at`, `updated_at` |
| `board_lanes` | `board_id`, `survey_id`, `order`, `title` |
| `business_labels` | `survey_id`, `node_id`, `code_hash`, `text`, `origin`, `state`, `updated_at` |
| `business_kind_overrides` | `survey_id`, `node_id`, `kind` (o usuário troca o tipo detectado) |
| `board_layouts` | `board_id`, `step_id`, `x`, `y`, `w`, `h`, `updated_at` |
| `board_edge_layouts` | `board_id`, `from_step`, `to_step`, `points_json` |

`business_labels` e `business_kind_overrides` ficam por levantamento (não por quadro), para o mesmo fluxo ter o mesmo texto em qualquer quadro. O layout fica por quadro (e, na visão de um levantamento sozinho, por levantamento; ver seção 0).

A visão de negócio em si **não** é gravada: é derivada a cada leitura (barata) e combinada com rótulos, tipos trocados e layout. Se ficar lenta em fluxo grande, guarde em cache por `(survey_id, schema_version)`.

## 12. API

| Método e rota | Função |
|---|---|
| `GET api/surveys/{id}/business` | `BusinessFlow` do levantamento, com rótulos e tipos trocados aplicados |
| `PUT api/surveys/{id}/business/steps/{nodeId}/label` | Texto do usuário (confirma) |
| `POST api/surveys/{id}/business/steps/{nodeId}/label/confirm` | Confirma a proposta atual |
| `PUT api/surveys/{id}/business/steps/{nodeId}/kind` | Troca o tipo |
| `POST api/surveys/{id}/business/labels/suggest` | Pede à IA os textos que faltam (uso "Sugestões") |
| `GET api/boards` / `POST api/boards` | Lista e cria quadros |
| `GET api/boards/{id}` | Quadro com as faixas, a visão de negócio de cada uma e o layout |
| `PUT api/boards/{id}` | Nome, glossário, ordem e título das faixas |
| `PUT api/boards/{id}/layout` | Layout em lote (a tela manda com debounce de 500 ms) |
| `DELETE api/boards/{id}/layout?lane={surveyId}` | Reorganizar a faixa |

Erros seguem o padrão atual da API do Rastro (códigos estáveis em `ErrorCodes`, mensagens nas três línguas). A exportação não tem rota: sai na tela (seção 0).

## 13. MCP

| Ferramenta | Entrada | Saída |
|---|---|---|
| `get_business_view` | `survey_id` ou `board_id` | Passos-chave com tipo, texto, estado, referência e ligações |
| `propose_business_label` | `survey_id`, `node_id`, `text` | Proposta gravada com origem `Agent`; o usuário confirma na tela |
| `propose_business_kind` | `survey_id`, `node_id`, `kind`, `reason` | Proposta de troca de tipo |
| `list_boards` | nenhuma | Quadros com as faixas |

As descrições ficam em inglês, como as outras. O retorno sai na língua escolhida.

## 14. Exportar

### 14.1 `.drawio`

Um `mxfile` com uma página. Cada faixa vira um retângulo tracejado com o título; cada passo, um `UserObject` com `link` (o link do host) e `tooltip`, e o `mxCell` com o estilo do tipo:

```text
Start        rounded=1;arcSize=50;whiteSpace=wrap;html=1;fillColor=#d5e8d4;strokeColor=#82b366;fontSize=12;fontStyle=1;
End          rounded=1;arcSize=50;whiteSpace=wrap;html=1;fillColor=#dae8fc;strokeColor=#6c8ebf;fontSize=12;
Step         rounded=1;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#666666;fontSize=12;
Decision     rhombus;whiteSpace=wrap;html=1;fillColor=#fff2cc;strokeColor=#d6b656;fontSize=11;
Notification rounded=1;whiteSpace=wrap;html=1;fillColor=#ffe6cc;strokeColor=#d79b00;fontSize=12;fontStyle=1;
Rejection    rounded=1;whiteSpace=wrap;html=1;fillColor=#f8cecc;strokeColor=#b85450;fontSize=12;
CrossSystem  rounded=1;whiteSpace=wrap;html=1;fillColor=#eeeeee;strokeColor=#999999;dashed=1;fontSize=10;align=left;spacingLeft=6;
Ligação      edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;endArrow=block;endFill=1;strokeColor=#555555;fontSize=11;fontStyle=1;labelBackgroundColor=#ffffff;
```

O `label` do `UserObject` é HTML: o texto de negócio e, abaixo, `<span style="font-size:9px;color:#555555;font-family:Consolas,monospace">repositório · arquivo:linha</span>`. Os pontos de dobra vão em `<Array as="points">`. O glossário e a legenda viram um `shape=note`.

Cuidados que apareceram no protótipo:

- Escape duplo: o HTML do rótulo é escapado como atributo XML (`&lt;br&gt;`), não como texto.
- Texto em losango precisa de espaço: use losango de 240×110 no mínimo e 280×150 para texto longo.
- Rótulo de ligação sobre linha que cruza: `labelBackgroundColor` resolve.

### 14.2 Mermaid, SVG e PNG

- Mermaid: `flowchart TD` com `subgraph` por faixa e `classDef` por tipo. Sem posição (o Mermaid não guarda).
- SVG e PNG: a própria superfície da tela, com as fontes embutidas.

## 15. Língua

Novas chaves nos três idiomas (en, pt-BR, es): nomes dos tipos, textos padrão por regra ("Envia e-mail", "Bloqueado na tela", "Recusa"), selos, botões e avisos. O texto de negócio do usuário não é traduzido. A IA escreve na língua escolhida no Rastro.

## 16. Testes

| Nível | O que cobre |
|---|---|
| Unidade (`Rastro.Core`) | Regras R1 a R7; decisão que não muda o resultado sai; agrupamento; limite de passos; rótulo do ramo; confiança inferida; ciclo |
| Unidade (links) | Remote HTTPS com usuário, SSH, Azure DevOps SSH v3, GitHub, GitLab, Bitbucket, remote desconhecido; caminho com espaço e acento |
| Unidade (export) | `.drawio` abre no draw.io (XML válido, `UserObject` com `link`), escape do HTML |
| Integração | Fixture `loja-web` + `api-pedidos` + `api-pagamentos` produz a tabela de 6.7 |
| Arquitetura | `Rastro.Core` continua sem referência a IA |
| Tela | Pan arrastando a área vazia; mover balão salva posição e sobrevive a recarregar; clique abre o painel; Ctrl + roda faz zoom no ponteiro; Reorganizar volta ao automático; teclado percorre e move |
| Desatualização | Mudar um commit marca a faixa; refazer mantém posição dos passos que ficaram e lista os que saíram |

## 17. Riscos

| Risco | Efeito | Mitigação |
|---|---|---|
| Padrão de notificação não cobre o projeto | Notificação sai como passo comum | Padrões configuráveis; troca de tipo pelo usuário ou pelo agente |
| Decisão relevante fundida por "não mudar o resultado" | Regra some do desenho | Usuário marca o nó como passo-chave no grafo ("Mostrar na visão de negócio"), gravado como troca de tipo |
| Texto da IA errado | Regra de negócio errada no desenho | Fica como proposto até confirmar; prompt proíbe inventar o que não está no código |
| Layout salvo quebra após refazer o levantamento | Balões sobrepostos | Casar por `symbol` + `location`; passo novo entra pelo layout automático; botão Reorganizar |
| Link para host errado | Clique leva a 404 | Usar commit; testes de URL por host; esconder o botão quando o remote não for reconhecido |
| Quadro com muitos fluxos fica lento | Tela travando | Virtualizar faixas fora da área visível; layout por faixa |
