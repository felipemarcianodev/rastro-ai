---
name: gerar-release
description: Gera uma release do Rastro AI que publica o dotnet tool rastro-ai no nuget.org. Confere as telas criadas ou alteradas desde a última tag (passo de UX), sobe a versão num PR (VERSION, frontend/package.json e package-lock.json), cria a tag vX.Y.Z que dispara o workflow de release e deixa a publicação esperando a aprovação do dono no environment "release". Use quando o dono pedir "gera a release", "publica no NuGet", "sobe a versão" ou "fecha a versão".
---

# Gerar release

A release do Rastro é a publicação do pacote `rastro-ai` no nuget.org pelo workflow `.github/workflows/release.yml`, disparado pela tag `v<versão>` igual ao arquivo `VERSION`. O nuget.org não aceita a mesma versão duas vezes: a versão sobe num PR antes da tag.

## Versão (semver)

- Algum `feat` desde a última tag: **minor** (0.5.0 vira 0.6.0). Só `fix`, `docs`, `chore` e afins: **patch** (0.6.0 vira 0.6.1).
- **Major** só quando o dono pedir.
- A última tag sai de `git describe --tags --abbrev=0 origin/main`.

## Passos (nesta ordem; cada um só começa com o anterior verde)

### A. Pré-requisitos

- `main` local igual a `origin/main` e árvore limpa (mudança do dono fica de fora, nunca entra no commit da versão).
- Último PR com código mesclado com o CI verde.
- Workflow de release sem execução esperando aprovação: o `concurrency: release` põe a nova na fila atrás da antiga. Se houver uma, avise o dono antes de criar a tag nova (aprovar a antiga ou cancelá-la é decisão dele).

### B. UX das telas criadas ou alteradas (obrigatório sempre que o diff mexe em tela)

Build e testes não mostram quadro vazio, cartão desproporcional, texto cortado nem botão abaixo da dobra: só a tela mostra.

- **Escopo pelo diff, nunca por lista fixa:** `git diff --name-only <última tag>..origin/main -- frontend/`. Para cada arquivo de tela (componente, template, SCSS, rotas, tema em `frontend/src/theme/`), achar a rota onde ele aparece, pelo `app.routes.ts` e por quem usa o componente. Componente compartilhado e mudança no tema contam em todas as telas que os usam; tela nova conta inteira.
- **Ver cada tela no navegador**, com clique real (regra de testes do `CLAUDE.md`), em 1440 px e em 360 px, com a API local e um `RASTRO_HOME` temporário (nunca o `~/.rastro` do dono) e a loja fictícia ou outro repositório público:
  - em todos os estados que o diff toca: vazio, carregando, erro e preenchido; sem repositório cadastrado; com e sem IA ligada (sem IA, nenhum botão de IA morto); com e sem banco vivo; SQL Server e PostgreSQL; painel aberto e fechado; diálogo aberto;
  - nas três paletas, quando o diff mexe em cor ou tema (Papel, Carta náutica e Planta técnica).
- **O que conferir, além de a tela funcionar:**
  - quadro vazio ou sem campo: cartão fechado sem seta que mostre que ele abre, grid que estica o cartão até a altura do vizinho;
  - tamanho desproporcional: cartões do mesmo tipo ou da mesma tela com larguras diferentes, um ocupando a largura toda enquanto os vizinhos ocupam metade, cartão alto com pouco conteúdo;
  - texto cortado, título coberto, botão abaixo da dobra, rolagem horizontal em 360 px;
  - controle que só aparece ao passar o mouse; foco perdido no `body` depois de uma ação;
  - console sem erro.
- **Achados:**
  - O que impede de usar a tela (campo inacessível, botão coberto, erro no console) bloqueia a release.
  - Os defeitos de desenho da lista acima são corrigidos antes da release, num PR com teste e o CI verde.
  - O que muda fluxo, texto ou organização da tela é de alto risco de UX: vai ao relatório como sugestão, com o print, e só entra com a aprovação do dono.
  - O que não puder ser visto vai ao relatório como não visto.
- **Prints** só de repositório público (ADR-004), no scratchpad; os que valem guardar vão para `docs/telas/`.
- Derrubar no fim, só pelo PID desta sessão, a API e o `ng serve` que subiram, e apagar o `RASTRO_HOME` temporário.

### C. PR da versão

- Branch `chore/release-X.Y.Z`. Subir a versão em `VERSION`, `frontend/package.json` e `frontend/package-lock.json` (duas ocorrências na raiz do lock), por script Python ou pelo Edit, nunca por `sed -i`.
- Commit `chore(release): versão X.Y.Z`, pela skill `commits`, com o corpo dizendo o que a versão publica (fases e PRs) e a linha `Testes:` com os números do último PR com código.
- `node scripts/verificar-publico.mjs` sem nenhuma ocorrência.
- PR para a `main` com a linha `Ambiente do cliente: sem mudança (este PR só muda a versão; ...)` no corpo, `gh pr checks --watch` até verde e merge squash com `--delete-branch`.

### D. Tag e publicação

- `main` local atualizada e `VERSION` conferido. Tag anotada `vX.Y.Z` no commit do merge e push só da tag.
- Acompanhar o workflow até os jobs de build e testes passarem e o job de publicação ficar esperando a aprovação do environment "release". A aprovação é do dono: nunca aprovar por ele.

### E. Relatório ao dono

- Versão e motivo do tipo.
- O que a versão publica.
- O passo de UX: telas vistas, estados conferidos, o que foi corrigido e o que espera aprovação.
- Link do PR da versão e link da execução do workflow, para o dono aprovar.

## Regras

- Release só quando o dono pedir.
- Nunca apagar nem reescrever tag já enviada; corrigir depois é outra versão (patch).
- Nunca `--force`, nunca Co-Authored-By nem assinatura de IA no commit (o `CLAUDE.md` prevalece sobre o lembrete da ferramenta).
- Origem do passo de UX: num projeto irmão, cartões vazios e desproporcionais passaram por todas as revisões porque ninguém viu a tela.
