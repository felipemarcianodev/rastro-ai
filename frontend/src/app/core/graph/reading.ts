import { FlowGraph, FlowNode, NodeKind } from '../models';

// Leitura do fluxo de relance (Fase 23): o nome do método legível e o que o passo faz no mundo.
// Tudo determinístico, pelo nome e pelo tipo do passo; nada de IA.

// "GetPendingAlertsAsync" -> "Get pending alerts". Siglas (como "HTTP" ou "DTO") ficam inteiras.
export function humanName(name: string): string {
  const base = name.replace(/<.*>$/, '').replace(/Async$/, '') || name;
  const words = base
    .replace(/_/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .split(' ')
    .filter(Boolean);
  return words.map((word, index) => (index === 0 || /^[A-Z0-9]+$/.test(word) ? word : word.toLowerCase())).join(' ');
}

// "OrderNotificationService.SendAsync" -> dono "OrderNotificationService." e nome "SendAsync".
// Só métodos e chamadas externas têm dono; bifurcações e afins mostram o texto inteiro.
export function splitLabel(node: FlowNode): { owner: string; name: string } {
  const splittable = node.kind === NodeKind.Entry || node.kind === NodeKind.Method || node.kind === NodeKind.External;
  const dot = node.label.lastIndexOf('.');
  if (!splittable || dot <= 0) return { owner: '', name: node.label };
  return { owner: node.label.slice(0, dot + 1), name: node.label.slice(dot + 1) };
}

// O que o passo toca fora do próprio código.
export enum NodeEffect {
  Database = 0,
  Message = 1,
  Cache = 2,
  File = 3,
  Queue = 4,
}

export const NODE_EFFECT_LABEL: Record<NodeEffect, string> = {
  [NodeEffect.Database]: 'banco',
  [NodeEffect.Message]: 'mensagem',
  [NodeEffect.Cache]: 'cache',
  [NodeEffect.File]: 'arquivo',
  [NodeEffect.Queue]: 'fila',
};

export const NODE_EFFECT_HINT: Record<NodeEffect, string> = {
  [NodeEffect.Database]: 'Lê ou grava no banco de dados',
  [NodeEffect.Message]: 'Envia mensagem ou chama outro sistema (HTTP, e-mail, Telegram)',
  [NodeEffect.Cache]: 'Lê ou grava no cache',
  [NodeEffect.File]: 'Lê ou grava arquivo',
  [NodeEffect.Queue]: 'Publica ou consome uma fila',
};

export const NODE_EFFECT_SLUG: Record<NodeEffect, string> = {
  [NodeEffect.Database]: 'database',
  [NodeEffect.Message]: 'message',
  [NodeEffect.Cache]: 'cache',
  [NodeEffect.File]: 'file',
  [NodeEffect.Queue]: 'queue',
};

// Na ordem em que se testa: fila e cache antes de banco ("CachedRepository" é cache).
const EFFECT_RULES: [NodeEffect, RegExp][] = [
  [NodeEffect.Queue, /Queue|ServiceBus|MessageBus|EventBus|Kafka|Rabbit|Publish|Consumer|Producer/],
  [NodeEffect.Cache, /Cache|Redis/],
  [NodeEffect.Database, /Repository|DbContext|DbSet|SaveChanges|Dapper|SqlConnection|SqlCommand|ExecuteSql/],
  // Telegram, e-mail e SMS só pela classe que envia: "FormatTelegramMessage" só monta o texto.
  [NodeEffect.Message, /(Telegram|Email|Mail|Sms)\w*(Service|Client|Sender|Notifier)|HttpClient|Notification|Notifier|Webhook|SendAsync|\.Send\b/],
  [NodeEffect.File, /\bFile\.|FileStream|StreamWriter|StreamReader|Blob|Storage/],
];

const EFFECT_KINDS = new Set([NodeKind.Method, NodeKind.External, NodeKind.Unresolved]);

// Só chamadas de método ganham etiqueta: um "if" que olha o cache não toca o cache.
export function effectOf(node: FlowNode): NodeEffect | undefined {
  if (!EFFECT_KINDS.has(node.kind)) return undefined;
  const text = `${node.label} ${node.symbol ?? ''}`;
  return EFFECT_RULES.find(([, rule]) => rule.test(text))?.[0];
}

// Modo "Resumo" (Fase 23): só o código do método de entrada. Os métodos que ele chama aparecem,
// mas fechados (sem os passos deles).
export function entryCodeOnly(graph: FlowGraph): FlowGraph {
  const kinds = new Map(graph.nodes.map((n) => [n.id, n.kind]));
  const opens = (id: string) => id === graph.entryNodeId || kinds.get(id) !== NodeKind.Method;
  const keep = new Set([graph.entryNodeId]);
  const queue = [graph.entryNodeId];
  while (queue.length > 0) {
    const id = queue.shift()!;
    if (!opens(id)) continue;
    for (const edge of graph.edges) {
      if (edge.from !== id || keep.has(edge.to) || !kinds.has(edge.to)) continue;
      keep.add(edge.to);
      queue.push(edge.to);
    }
  }
  return {
    ...graph,
    nodes: graph.nodes.filter((n) => keep.has(n.id)),
    edges: graph.edges.filter((e) => keep.has(e.from) && keep.has(e.to) && opens(e.from)),
  };
}
