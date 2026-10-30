---
name: executar-plano-faseado
description: Executa um plano faseado em markdown do Rastro AI (`docs/arquitetura/plano-*.md`, `docs/frontend/plano-ux-*.md`) fase a fase, com os portões combinados (build e testes do backend, build do frontend, correção até verde), status registrado no próprio plano, commit num branch e PR para a main, e só então a próxima fase. Usa até 3 agentes Sonnet por fase com posse exclusiva de arquivos e o Opus coordenando. Use quando o usuário disser "execute o plano", "pode seguir com as fases", "faça fase a fase", ou apontar um arquivo de plano faseado.
---

# Executar plano faseado

## Regras do dono (não negociáveis)
- **Uma fase por vez.** Antes da próxima, todos verdes:
  - Backend: `dotnet build backend/Rastro.slnx` (0 erros) e `dotnet test backend/Rastro.slnx` (0 falhas; Testcontainers quando houver banco);
  - Frontend: `cd frontend && npm run build` (sem erros nem warnings novos) e `npm test -- --watch=false` quando houver specs.
  Erro: corrigir até verde, entendendo a causa. Só então commit, push do branch e PR.
- **UX:** fase com mudança grande no frontend (tela nova, ou 5+ elementos novos numa tela) termina com a skill **`levantamento-ux`** sobre as telas tocadas. Os achados de baixo e médio risco entram na própria fase; os de alto risco viram itens `[ ]` no plano e esperam aprovação. **No fim do plano, `levantamento-ux` completo.**
- **IA plugável (ADR-001):** nenhuma fase coloca código de IA em `Rastro.Core` ou `Rastro.Extractor`. O teste de arquitetura precisa continuar verde.
- **Commit:** `tipo(escopo): descrição` em pt-BR com acentuação, corpo com o que mudou e a linha `Testes: ...` com números reais; **sem** `Co-Authored-By` nem assinatura de IA. Push do branch da fase e PR para a `main` (protegida no GitHub, merge squash), nunca `--force`. Antes do PR, `node scripts/verificar-publico.mjs` sem nenhuma ocorrência (o repositório é público). Adicionar só os arquivos da fase (conferir `git status`).
- **Status no mesmo commit:** o próprio plano é o registro (fase `[x]`, o que foi feito, sub-itens descobertos como `[ ]`).
- **Checkpoint ou "requer aprovação":** parar e mostrar ao dono antes de seguir. Nunca assumir aprovação.
- **Release** só quando o dono pedir.

## Antes de começar
1. Ler o plano inteiro, o `CLAUDE.md`, `docs/arquitetura/visao.md` e as ADRs em `docs/adr/`.
2. `git status` limpo (separar mudanças do dono), `git fetch` e `main` igual a `origin/main`.
3. Decisão em aberto que muda o desenho: perguntar antes, uma vez, agrupado.

## Por fase
1. **Contrato:** fase que mexe em backend e frontend começa pelo contrato (rotas, DTOs, enums, erros) num arquivo no scratchpad.
2. **Agentes (até 3, `model: sonnet`, em paralelo):** posse exclusiva de pastas (Backend, Frontend, Docs). Cada prompt leva a fase, o contrato, as regras aplicáveis, "NÃO faça commit nem push", "não crie sub-agentes" e o que reportar. Não inventar trabalho.
3. **Integração (Opus):** conferir divergências entre os agentes (nomes, rotas, enums, formatos) e alinhar a doc ao código.
4. **Ambiente do cliente:** se a fase mexe no que quem instala o Rastro precisa ter, configurar ou saber (dependência na máquina, pacote, pasta de dados, migration, porta, CLI, MCP, linguagem suportada, o que sai da máquina, CI e release), atualizar no mesmo PR o `README.md`, o `docs/pacote/README.md`, a `visao.md`, a ADR, o `SECURITY.md`, as instruções do MCP, o `CLAUDE.md` e as skills que tratam disso (lista completa na seção "Mudança no ambiente do cliente" do `CLAUDE.md`).
5. **Portões**, UX quando couber, **commit, push do branch e PR**, aviso ao dono em 2 a 4 linhas, próxima fase.

## Fim do plano
Marcar o plano como EXECUTADO, rodar `levantamento-ux` completo, consolidar pendências e resumir ao dono.
