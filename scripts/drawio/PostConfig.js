/**
 * PostConfig.js do Rastro (ADR-011), copiado por scripts/preparar-drawio.ps1 por cima do original do draw.io.
 * O PostConfig.js é o arquivo que o draw.io reserva para quem hospeda o próprio editor. Este mantém o que o
 * original faz (desliga os serviços de busca de ícones na rede) e acrescenta um plugin pequeno, que avisa a tela
 * do Rastro qual balão foi escolhido, pelo rastroId e pelo rastroNode do UserObject, só para a mesma origem.
 */
window.ICONSEARCH_PATH = null;
window.ICON_SERVICE_PATH = null;

// O PostConfig.js roda depois do app.min.js e antes de o editor nascer, quando o window.Draw ainda não existe: a fila
// de plugins se cria aqui pelo próprio App.initPluginCallback (que não refaz uma fila já criada). No modo embutido, o
// editor espera cada plugin da fila descontar o App.embedModePluginsCount antes de ficar pronto, então este plugin
// soma um, como o App.loadPlugins faz com os plugins do próprio draw.io.
App.initPluginCallback();
if (urlParams['embed'] == '1') App.embedModePluginsCount++;

Draw.loadPlugin(function (ui) {
  var graph = ui.editor.graph;
  graph.getSelectionModel().addListener(mxEvent.CHANGE, function () {
    if (window.parent === window) return;
    var cell = graph.getSelectionCell();
    var value = cell != null ? cell.value : null;
    var read = function (name) { return value != null && typeof value.getAttribute === 'function' ? value.getAttribute(name) || null : null; };
    window.parent.postMessage(JSON.stringify({ event: 'rastro-select', rastroId: read('rastroId'), rastroNode: read('rastroNode') }), window.location.origin);
  });
});
