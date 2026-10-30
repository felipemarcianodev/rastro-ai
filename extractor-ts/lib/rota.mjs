// Normalização de rota HTTP, a mesma do C# (Rastro.Core.Http.RouteTemplate.Normalize).
// Precisa dar o mesmo resultado nos dois lados: é por igualdade de rota que a costura entre
// sistemas liga a chamada do Angular ao controller .NET.

// Marca de um pedaço não constante da URL chamada.
export const BURACO = '{}';

const ESQUEMA_E_HOST = /^[a-zA-Z][a-zA-Z0-9+.\-]*:\/\/[^/?#]*/;

// Tira esquema e host, query, fragmento e barras das pontas; minúsculas fora das chaves.
export function normalizarRota(rota) {
  let texto = rota.trim();
  texto = texto.replace(ESQUEMA_E_HOST, '');

  const corte = texto.search(/[?#]/);
  if (corte >= 0) texto = texto.slice(0, corte);

  if (texto.startsWith('~/')) texto = texto.slice(2);
  texto = texto.replace(/^\/+/, '').replace(/\/+$/, '');

  let resultado = '';
  let profundidade = 0;
  for (const c of texto) {
    if (c === '{') profundidade++;
    resultado += profundidade > 0 ? c : c.toLowerCase();
    if (c === '}' && profundidade > 0) profundidade--;
  }
  return resultado;
}

// Rota da chamada a partir dos pedaços coletados: [{ texto, buraco }].
// Os buracos que aparecem antes do primeiro texto são o endereço base (vem de configuração) e saem.
// Sem nenhum texto, a rota é montada em tempo de execução: devolve nulo.
export function rotaDosPedacos(pedacos) {
  const primeiro = pedacos.findIndex((p) => !p.buraco);
  if (primeiro < 0) return null;

  const rota = normalizarRota(
    pedacos
      .slice(primeiro)
      .map((p) => (p.buraco ? BURACO : p.texto))
      .join(''),
  );
  return rota.length === 0 ? null : rota;
}
