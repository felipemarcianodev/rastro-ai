# Rastro AI

Segue o rastro de uma funcionalidade no seu código .NET, da entrada (controller, job, handler) ao fim, com todas as bifurcações e a implementação real atrás de cada interface do DI. Mostra o fluxo como grafo editável e gera o prompt exato para a IA alterar aquele ponto.

O Rastro roda só na sua máquina: o código nunca sai dela.

## Instalar

Requer o SDK do .NET 10.

```
dotnet tool install -g rastro-ai
```

## Usar

```
rastro
```

A tela abre em http://127.0.0.1:7450 (só nesta máquina; troque a porta com `--porta`). Cadastre os repositórios em Configurações (a engrenagem no alto da tela) ou pelo terminal:

```
rastro repo add minha-api C:\codigo\minha-api
```

Para o build e os testes entrarem na validação de uma mudança, informe os comandos ao cadastrar pelo terminal:

```
rastro repo add minha-api C:\codigo\minha-api --build "dotnet build MinhaApi.slnx" --testes "dotnet test MinhaApi.slnx"
```

## Fluxo entre sistemas

Quando uma API chama outra por HTTP, o fluxo continua do outro lado. Ao criar o levantamento, escolha **Seguir chamadas HTTP para** e marque os outros repositórios (eles precisam estar cadastrados no Rastro). O Rastro casa o verbo e a rota de cada chamada com as rotas desses repositórios e segue o rastro no método que atende, com as bifurcações de cada sistema no mesmo desenho.

O que não dá para casar vira um passo "não resolvido", com o motivo: rota montada em tempo de execução, sem destino, ou mais de um destino. A IA (com uma chave de API em Configurações) ou o seu agente completa esse passo, e você confirma na tela.

Limites: a edição e a validação de uma mudança valem só para o primeiro sistema do levantamento, e o Rastro mantém uma solution aberta por vez.

## Visão consolidada

Cada levantamento tem uma visão resumida em poucos passos, em frase, como um diagrama Mermaid que você edita ao lado da prévia. Clique num passo para escolher a cor dele. A versão editada fica salva no levantamento.

## Com o seu agente de IA (MCP)

O Rastro funciona sem IA. Com um agente que fala MCP, como o Claude Code, o agente completa o que o compilador não resolveu e escreve as regras de cada passo, usando a assinatura dele:

```
claude mcp add rastro -- rastro mcp
```

Para o fluxo entre sistemas, a ferramenta `list_cross_edges` lista as ligações HTTP e as chamadas ainda sem destino, e `propose_edge` aceita o parâmetro `system`, com o repositório de destino.

Tudo o que a IA propõe chega marcado como inferido e só entra no fluxo quando você confirma.

## Licença

Código aberto, licença MIT: <https://github.com/felipemarcianodev/rastro-ai>.

## Seus dados

Ficam em `~/.rastro` (banco SQLite e repositórios cadastrados). Para usar outra pasta, defina a variável `RASTRO_HOME`.

## Atualizar e remover

Pare o `rastro` antes (no Windows, o executável em uso não pode ser trocado):

```
dotnet tool update -g rastro-ai
dotnet tool uninstall -g rastro-ai
```
