# ADR-001: IA opcional e plugável (núcleo sem IA, modo agente e modo provedor)

**Data:** 2026-10-15
**Status:** Accepted
**Contexto:** Define como o Rastro AI usa IA sem depender dela nem de um fornecedor.

---

## Contexto

O Rastro levanta o fluxo de uma funcionalidade com o Roslyn (determinístico) e usa IA em quatro pontos: completar os buracos do grafo, descrever as regras em linguagem humana, sugerir melhorias e sugerir pessoas e sistemas externos no C1.

Restrições do dono:
- Não depender de um fornecedor de IA (Anthropic, OpenAI, Google).
- Poder usar o Rastro a partir de um agente que já tem assinatura (ex.: Claude Code), **sem chave de API**.
- Seguir o padrão já usado no `meu-portfolio` para configurar provedores (tela de configuração, chave cifrada, modelo por provedor).
- A decisão precisa ser **visível** para quem pegar o código: na estrutura de pastas, no README, na tela e nos testes, e não só neste documento.

## Decisão

### Decisão 1: a IA é opcional

| Opção | Descrição | Prós | Contras |
|---|---|---|---|
| A. IA obrigatória | O fluxo só fecha com IA | Grafo mais completo sempre | Custo e dependência em todo uso; sem IA, nada funciona |
| B. **IA opcional** | O núcleo funciona sem IA; a IA só acrescenta | Sem custo para o uso básico; sem dependência | Sem IA, os buracos ficam marcados para o usuário resolver |

**Decisão: B.** Extração, grafo, edição, geração de prompt, métricas e C4 funcionam **sem IA nenhuma**. Sem IA, os nós não resolvidos aparecem marcados com o motivo, e o usuário pode ligá-los à mão (`confirmed`).

### Decisão 2: dois modos de IA, com a mesma porta de entrada

| Opção | Descrição | Prós | Contras |
|---|---|---|---|
| A. Só provedor | O Rastro chama a API de IA | Automático | Exige chave de API e paga por uso |
| B. Só agente (MCP) | Um agente externo usa o Rastro | Sem chave; usa a assinatura que o dono já tem | Exige um agente rodando; não é automático na tela |
| C. **Os dois, plugáveis** | Agente e provedor entregam propostas ao mesmo núcleo | Cada um usa o que tem | Duas integrações para manter |

**Decisão: C.**

- **Modo agente:** o projeto `Rastro.Mcp` expõe ferramentas MCP (`analyze_flow`, `list_unresolved`, `get_node_code`, `propose_edges` etc.). Quem pensa é o agente.
- **Modo provedor:** o projeto `Rastro.Ai` chama Claude, OpenAI ou Gemini, escolhidos na tela de feature flags, no padrão do `meu-portfolio` (`AiProvider`, `AiSettings`, `IAiTextService` por provedor, chave cifrada com DataProtection).
- **A mesma porta:** os dois modos só **propõem**. Toda proposta (aresta inferida, descrição, sugestão) entra pelo núcleo e passa pelo **validador de prova** (o símbolo existe naquele `arquivo:linha`?). Nenhum modo grava direto no grafo.

```csharp
// Rastro.Core: o núcleo conhece propostas, não conhece IA.
public enum ProposalOrigin { Manual = 0, Agent = 1, Provider = 2 }

public record EdgeProposal(string FromNodeId, string ToSymbol, SourceLocation Evidence,
                           string Reason, ProposalOrigin Origin);

public interface IProposalValidator
{
    ProposalVerdict Validate(FlowGraph graph, EdgeProposal proposal);
}
```

### Decisão 3: a separação fica visível e é garantida por teste

| Onde | Como fica visível |
|---|---|
| Pastas | `Rastro.Core` e `Rastro.Extractor` (sem IA), `Rastro.Ai` (modo provedor), `Rastro.Mcp` (modo agente) |
| Teste de arquitetura | Falha se `Rastro.Core` ou `Rastro.Extractor` referenciarem `Rastro.Ai` ou `Rastro.Mcp` |
| README | Seção "IA plugável" logo no início, com os três jeitos de usar |
| Tela | Feature flags com o modo de IA (desligada / provedor) e um bloco "Modo agente" explicando como conectar o MCP |
| Grafo | Toda aresta mostra a origem (`extracted`, `inferred` por agente ou provedor, `confirmed`) |
| `CLAUDE.md` | Regra bloqueante: código de IA nunca entra no núcleo |

## Análise de riscos

| Risco | Probabilidade | Impacto | Mitigação |
|---|---|---|---|
| Código de IA "vazar" para o núcleo com o tempo | Média | Alto | Teste de arquitetura quebra o build; regra no `CLAUDE.md` |
| IA (agente ou provedor) inventa aresta | Alta | Alto | Validador de prova obrigatório; sem prova, recusa com o motivo |
| Provedores com formatos de saída diferentes | Média | Médio | Cada implementação converte para o mesmo DTO; JSON validado contra schema |
| Chave de API exposta | Baixa | Alto | Cifrada com DataProtection, nunca volta pela API, nunca vai para log |

## Consequências

### Positivas
- O Rastro funciona de graça e sem internet no uso básico.
- Trocar ou acrescentar provedor não toca no núcleo.
- O dono usa o Rastro pelo Claude Code, com a assinatura que já paga.

### Negativas
- Duas integrações para manter (MCP e provedores). Mitigação: as duas são finas e só produzem `Proposal`.
- No modo agente, a qualidade depende do agente usado. Mitigação: o validador de prova vale para todos.

## O que NÃO fazer

- Não chamar IA de dentro do `Rastro.Core` ou do `Rastro.Extractor`.
- Não gravar no grafo nada que veio de IA sem passar pelo validador.
- Não esconder a origem de uma aresta: o usuário sempre vê quem a criou.
