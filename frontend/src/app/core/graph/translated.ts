import { tr } from '../i18n/locale';

// Record de texto que traduz na hora da leitura: cada valor do mapa de chaves vira um acessor
// que chama tr(). Quem lê dentro de um computed ou de um template se refaz na troca de idioma,
// e o nome antigo da constante continua valendo para quem a importa.
export function translatedRecord<K extends string | number>(keys: Record<K, string>): Record<K, string> {
  const result = {} as Record<K, string>;
  for (const key of Object.keys(keys) as unknown as K[]) {
    Object.defineProperty(result, key, { enumerable: true, get: () => tr(keys[key]) });
  }
  return result;
}
