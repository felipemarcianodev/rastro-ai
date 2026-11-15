# ADR-009: Rastro em inglês, português e espanhol

**Data:** 2026-11-15
**Status:** Accepted
**Contexto:** Pedido do dono em 2026-11-15. Plano nas Fases 45 a 53 do [`plano-mvp.md`](../arquitetura/plano-mvp.md#rastro-multilíngue-fases-45-a-53-pedido-do-dono-em-2026-11-15).

---

## Contexto

O Rastro é público ([ADR-004](adr-004-codigo-aberto.md)) e distribuído pelo NuGet ([ADR-002](adr-002-produto-dotnet-tool.md)), mas tudo está em português: a tela, as mensagens da API, a CLI, o MCP, os textos que o núcleo gera e a documentação. Quem não lê português não consegue usar nem avaliar o projeto.

O levantamento de 2026-11-15 contou cerca de 1.000 textos nas telas, 450 linhas de rótulo no TypeScript, 650 linhas de texto no backend, 190 no MCP e 6 prompts de provedor de IA que pedem a resposta em português. Não havia nenhuma infraestrutura de tradução, e `pt-BR` estava fixo em seis pontos de formatação.

Outro projeto do dono já é multilíngue, com `IStringLocalizer` e `.resx` no backend, ngx-translate no frontend e o idioma no cabeçalho `Accept-Language`. O padrão funciona, mas tem quatro dívidas: nenhuma conferência de chave faltando, idioma salvo que o servidor não lê, plural escrito como "(s)" e data formatada sem o idioma ativo.

## Decisão

### 1. Três idiomas

| Idioma | Código | Papel |
|---|---|---|
| Inglês | `en-US` | principal da documentação; reserva quando o sistema está num idioma que o Rastro não tem |
| Português | `pt-BR` | o idioma de hoje |
| Espanhol | `es` | neutro latino-americano, tratamento por "tú"; casa com `es-ES`, `es-MX` e `es-AR` |

O chinês simplificado foi proposto e retirado pelo dono.

### 2. Como o idioma é escolhido

1. A escolha fica em `~/.rastro/config.json` (`"Language"`), gravada pela tela (rota com a proteção de `Origin` da [ADR-003](adr-003-repositorios-pela-tela.md)) e lida pela API, pela CLI e pelo MCP.
2. Sem a chave, vale o idioma do sistema operacional, se for um dos três; se não for, inglês.
3. A tela manda `Accept-Language` em toda chamada. A API responde no idioma pedido; sem o cabeçalho (curl, agente), usa o do `config.json`.

### 3. Biblioteca

| Opção | Prós | Contras |
|---|---|---|
| `@angular/localize` | nativo, sem dependência | um build por idioma; trocar de idioma recarrega outra pasta; não cabe num `dotnet tool` com uma tela só |
| Transloco | carregamento por escopo | sem ganho que pague destoar do padrão que o dono já conhece |
| **ngx-translate com ICU** | troca na hora, padrão conhecido, plural com o compilador de MessageFormat | dependência nova, chave em texto (coberta pelo teste de paridade) |

**Decisão:** ngx-translate com o compilador de MessageFormat no frontend; `IStringLocalizer` com `.resx` no backend, um conjunto de recursos por projeto que gera texto (para não virar um arquivo único com conflito de merge). O núcleo continua sem referência à API e à IA ([ADR-001](adr-001-ia-opcional-e-plugavel.md)): recebe a cultura como parâmetro.

### 4. Onde cada texto é traduzido

| Tipo de texto | Quem traduz | Exemplo |
|---|---|---|
| Dado estruturado | a tela, pelo `Record` | status, tipo de achado, gravidade, código de erro, motivo de recusa |
| Prosa gerada | o servidor, pelo `.resx`, no idioma do pedido | prompt da mudança, documentação do fluxo, relatório da validação, saída do MCP |
| Texto gravado | ninguém: fica como nasceu | regra escrita pela IA, nota do usuário, prosa já gerada |
| Código do cliente | nunca se traduz | nome de método e de tabela, trecho de código, caminho, mensagem do compilador e do banco |

O servidor passa a mandar código e parâmetros no que hoje é texto pronto (erro do levantamento, motivo de recusa de proposta). O texto antigo gravado continua aparecendo como está.

### 5. Determinismo

A regra da validação, do prompt e da documentação passa a ser: mesmo código, mesmo rascunho e **mesmo idioma** dão o mesmo resultado. A cultura entra como parâmetro explícito, nunca como a cultura da thread, e há teste byte a byte por idioma.

### 6. IA

- O prompt gerado para a IA sai no idioma da tela.
- As instruções e as descrições das ferramentas do MCP ficam em inglês (o agente as lê uma vez, ao conectar) e pedem a resposta no idioma do usuário. A saída das ferramentas segue o `config.json`.
- Os prompts dos provedores recebem o idioma como parâmetro. O validador de prova não muda.

### 7. Documentação

- Vai para os três idiomas o que quem usa lê: `README.md` (em inglês) com `README.pt-BR.md` e `README.es.md`, o README do pacote e a `<Description>` do NuGet (em inglês, com links para os outros), e o `SECURITY.md`.
- Ficam em português o material de trabalho do dono: plano, ADRs, planos de UX, `CLAUDE.md`, skills, `docs/telas/`, mensagens de commit e de PR.
- A tradução sai da IA a partir do [glossário](../i18n/glossario.md). Não há falante nativo de espanhol revisando; o README convida correção por PR.

### 8. Conferência automática

- Teste de paridade nos JSON do frontend e nos `.resx` do backend: toda chave existe nos três idiomas, com os mesmos marcadores (`{0}`, `{count}`).
- Varredura dos templates atrás de texto fixo fora do `translate`.
- Script no workflow de PR que confere os três README com as mesmas seções.
- O hook `revisao-ptbr` ignora arquivo com outro idioma no nome (`README.es.md`); os JSON de tradução não são prosa e já ficavam de fora.

## Análise de riscos

| Risco | Probabilidade | Impacto | Mitigação |
|---|---|---|---|
| Termo errado em espanhol | Média | Médio | Glossário único; correção por PR aberta no README |
| Texto novo entrar só em português | Alta | Médio | Teste de paridade e varredura de texto fixo no CI |
| Prompt deixar de ser determinístico | Baixa | Alto | Cultura como parâmetro explícito; teste byte a byte por idioma |
| Texto em espanhol estourar botão e cartão | Alta | Baixo | Fase 53 inteira de conferência visual, nas três paletas e em 360 px |
| Histórico gravado misturar idiomas | Média | Baixo | O que é estruturado passa a código e se traduz sozinho; o resto fica como nasceu, por regra |

## Consequências

### Positivas
- Quem não lê português usa o Rastro e lê a documentação.
- A conferência de chave no CI impede que um idioma fique para trás sem ninguém perceber.
- Um quarto idioma passa a custar um JSON, os `.resx` e três README.

### Negativas
- Todo texto novo de tela entra em três JSON no mesmo PR. Mitigação: a skill `traduzir-texto` e o teste de paridade.
- A documentação para quem usa é mantida em três idiomas. Mitigação: o script de seções no CI e o hook de ambiente do cliente.

## O que NÃO fazer

- Não usar a cultura da thread para gerar prompt, documentação ou relatório: passe a cultura como parâmetro.
- Não comparar texto traduzido no código: compare pelo enum ou pelo código, como manda o `CLAUDE.md`.
- Não traduzir o código do cliente nem o que a IA já escreveu.
- Não retraduzir em silêncio o texto gravado.
