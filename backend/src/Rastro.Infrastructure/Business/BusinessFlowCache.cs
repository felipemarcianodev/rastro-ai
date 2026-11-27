using System.Security.Cryptography;
using System.Text;
using Rastro.Core.Business;

namespace Rastro.Infrastructure.Business;

// Cache da derivação da visão de negócio (sem os textos gravados, que se aplicam depois e mudam a cada edição).
// Um fluxo de 483 nós levava de 500 a 800 ms para derivar, a cada abertura da tela e a cada gravação de texto.
//
// A chave é o conteúdo, não o id: o grafo do levantamento muda depois da análise (proposta aceita, regra
// escrita) e o MCP é outro processo, que grava no mesmo banco. Por isso a chave junta o hash do grafo, dos trechos,
// das opções do config.json, dos tipos gravados e do idioma; qualquer mudança dá outra chave, e a entrada antiga sai
// pelo limite. Guarda poucos fluxos (os da tela aberta), do mais recente para o mais antigo.
public sealed class BusinessFlowCache
{
    public const int Capacity = 32;

    private readonly Lock gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, BusinessFlow Flow)>> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, BusinessFlow Flow)> recent = new();

    public int Count
    {
        get
        {
            lock (gate) return entries.Count;
        }
    }

    // A derivação roda fora da trava: duas leituras ao mesmo tempo do mesmo fluxo podem derivar duas vezes, o que
    // só custa tempo (o resultado é o mesmo, porque a derivação é determinística).
    public BusinessFlow GetOrAdd(string key, Func<BusinessFlow> derive)
    {
        lock (gate)
        {
            if (entries.TryGetValue(key, out var hit))
            {
                recent.Remove(hit);
                recent.AddFirst(hit);
                return hit.Value.Flow;
            }
        }

        var flow = derive();
        lock (gate)
        {
            if (entries.TryGetValue(key, out var raced)) return raced.Value.Flow;
            entries[key] = recent.AddFirst((key, flow));
            while (entries.Count > Capacity && recent.Last is { } oldest)
            {
                recent.RemoveLast();
                entries.Remove(oldest.Value.Key);
            }
        }
        return flow;
    }

    // Hash de tudo o que muda o resultado da derivação, na ordem em que vem.
    public static string KeyOf(params string[] parts)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\u001f', parts));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
