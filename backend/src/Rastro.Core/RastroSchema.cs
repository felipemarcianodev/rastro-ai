namespace Rastro.Core;

// Versão do formato JSON do grafo. Sobe a cada mudança do schema.
// 2 (Fase 25): nó com sistema e aresta com chamada HTTP. Grafo de versão 1 continua sendo lido,
// porque os campos novos são opcionais.
// 3 (Fase 34): chamada HTTP com os campos enviados (Fields e MoreFields). Grafo de versão 2 continua
// sendo lido: os dois campos são opcionais.
public static class RastroSchema
{
    public const int Version = 3;
}
