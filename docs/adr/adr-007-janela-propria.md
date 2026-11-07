# ADR-007: janela própria ao digitar `rastro`, com o Photino.NET

**Data:** 2026-11-06
**Status:** Accepted
**Contexto:** Complementa a [ADR-002](adr-002-produto-dotnet-tool.md) (o Rastro é um `dotnet tool`) e segue a [ADR-003](adr-003-repositorios-pela-tela.md) (a API só na máquina, protegida contra pedido de outro site).

---

## Contexto

O dono quer que o `rastro` abra como um app, numa janela própria, sem passar pelo navegador. Hoje o comando sobe a API em `127.0.0.1:7450` e o usuário abre o endereço no navegador.

Junto veio um segundo pedido: um botão "Procurar…" ao lado do campo da pasta do repositório, que abre a caixa "Escolher pasta" do sistema e devolve o caminho completo. O navegador não entrega o caminho completo da pasta escolhida a uma página, então o botão só é possível numa janela que o próprio Rastro controla.

As condições: o pacote continua sendo um `dotnet tool` (nada de instalador), não cresce dezenas de MB, a tela continua abrindo no navegador como hoje, e o MCP e a CLI não mudam.

## Opções

| Opção | Como funciona | Peso no pacote | Caixa "Escolher pasta" |
|---|---|---|---|
| **Photino.NET** | Janela nativa aberta pelo próprio executável .NET, com o WebView do sistema (WebView2 no Windows, WebKit no macOS e no Linux) | cerca de 2 MB para os seis sistemas (cerca de 1 MB a mais no `.nupkg`) | sim, nativa, pela ponte de mensagens |
| Navegador em modo app (`chrome --app=URL`, `msedge --app=URL`) | O Rastro lança o navegador instalado sem barra de endereço | nada | não: a página continua sem o caminho completo |
| Electron | Chromium e Node dentro do pacote | cerca de 150 MB por sistema | sim |

O modo app depende de qual navegador está instalado e onde, abre um processo externo escolhido pelo Rastro e não resolve o "Procurar…". O Electron é recusado pelo tamanho e por trazer um segundo runtime (Node) para dentro de um `dotnet tool`.

## Decisão

1. **Photino.NET 4.0.16** (pacote `Photino.NET`, que traz o `Photino.Native` 4.0.22), licença **Apache-2.0**, compatível com a MIT do Rastro. O Rastro não copia nem altera código do Photino; a dependência fica registrada no README, na seção de terceiros.
2. **Quando abre:** o `rastro` sobe a API como hoje e abre a janela apontando para o mesmo endereço local, só quando é o pacote rodando de verdade:
   - não abre dentro dos testes (`WebApplicationFactory`);
   - não abre com endereço configurado (`dotnet run` com o `launchSettings`, `--urls`, Docker);
   - não abre sem a tela embutida (`wwwroot`), como no desenvolvimento com o `ng serve`;
   - não abre nos comandos da CLI (`rastro mcp`, `rastro repo`), que saem antes;
   - `rastro --sem-janela` mantém o comportamento de antes: só o console e o endereço.
3. **Ciclo de vida:** a API roda em segundo plano e a janela, na thread principal (no Windows, numa thread STA, como o WebView2 exige; no macOS, a thread principal do processo, por isso o `Program` não tem `await` antes da janela). Fechar a janela encerra o Rastro; Ctrl+C no console fecha a janela.
4. **Se a janela não pode abrir, a API continua.** Antes de criar a janela, o Rastro confere o que ela precisa e, faltando algo, avisa no console, registra no log e segue como no `--sem-janela`, com o endereço para o navegador:
   - Windows: sessão com tela e o **WebView2 Runtime** (conferido pela função oficial do carregador `WebView2Loader.dll`, que viaja no pacote). A conferência vem antes porque o Photino, sem o runtime, tenta baixar e instalar o WebView2 sozinho, e o Rastro não faz download sem o usuário pedir;
   - Linux: `DISPLAY` ou `WAYLAND_DISPLAY` e a biblioteca `libwebkit2gtk-4.1` (o GTK sem tela encerra o processo, então isso também é conferido antes);
   - macOS: o WebKit é do sistema.
   Falha ao criar a janela depois da conferência também cai no console, com o erro no log.
5. **Ponte de mensagens com formato fixo.** A tela fala com o .NET por `window.external.sendMessage` e `receiveMessage`, que só existem na janela. A única mensagem aceita é `{"type":0,"requestId":"pasta-N"}` (escolher pasta), com no máximo 512 caracteres, sem outro campo e com o `requestId` só de letras, números e hífen. A resposta é `{"type":1,"requestId":"...","path":"..."}`, com `path` absoluto ou `null` (cancelou). O resto é recusado e registrado no log. Nenhuma mensagem executa comando, lê disco ou chama a API: o caminho escolhido só preenche o campo, e o cadastro continua passando pelo `POST api/repositories`, com as regras da ADR-003.
6. **Bibliotecas nativas no `dotnet tool`:** o `dotnet pack` leva a pasta `runtimes/` do `Photino.Native` para dentro do pacote (`tools/net10.0/any/runtimes/<rid>/native/`: `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64` e `osx-arm64`), e o `deps.json` aponta a biblioteca de cada sistema. Conferido no pacote instalado: a janela abre no Windows sem nenhum passo a mais no `scripts/empacotar.ps1`.
7. **Dados da janela:** o cache e o armazenamento do WebView2 ficam em `~/.rastro/janela` (ou `RASTRO_HOME/janela`), e não ao lado do executável. As ferramentas de desenvolvedor do WebView ficam desligadas.

## Consequências

- **Ambiente do cliente:**
  - Windows: o WebView2 Runtime já vem no Windows 11 e nas versões atuais do Windows 10; sem ele, o Rastro avisa e abre só no console.
  - Linux: a janela pede a `libwebkit2gtk-4.1` (no Ubuntu e no Debian, `sudo apt install libwebkit2gtk-4.1-0`) e uma sessão com tela; sem isso, só o console, como antes.
  - O pacote cresce cerca de 1 MB; nada sai da máquina por causa da janela.
  - A pasta `~/.rastro/janela` passa a existir depois da primeira abertura.
- A janela tem o próprio armazenamento: a paleta e o último repositório escolhidos no navegador não aparecem na janela, e o contrário também.
- O `rastro` aberto uma segunda vez, com a porta já em uso pelo Rastro, continua só mostrando o endereço (não abre uma segunda janela).
- Para testar o pacote sem abrir janela (CI, sessões automatizadas), use `rastro --sem-janela`.
- **Fica para depois:** o "Procurar…" no navegador, por uma rota local da API com a proteção de origem das outras rotas, se o dono quiser; a janela no macOS e no Linux foi decidida pela documentação do Photino, mas só o Windows foi conferido na prática.
