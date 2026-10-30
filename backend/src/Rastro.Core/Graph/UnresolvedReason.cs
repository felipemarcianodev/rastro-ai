namespace Rastro.Core.Graph;

// Por que uma chamada ficou sem destino. É o que o agente, o provedor de IA ou o usuário
// recebem para completar o buraco (ADR-001).
public enum UnresolvedReason
{
    // Interface ou método abstrato com mais de uma implementação e sem registro de DI que decida.
    MultipleImplementations = 0,

    // Interface ou método abstrato sem implementação no código.
    NoImplementation = 1,

    // Chamada dinâmica ou que o compilador não conseguiu ligar a um símbolo.
    DynamicDispatch = 2,

    // Delegate, Func, Action ou evento invocado: o destino depende de quem atribuiu.
    Delegate = 3,

    // Mensagem enviada por mediator ou barramento: o handler é escolhido em tempo de execução.
    MessageDispatch = 4,

    // Reflection (MethodInfo.Invoke, Activator.CreateInstance).
    Reflection = 5,

    // Limite de profundidade atingido: o caminho continua, mas não foi percorrido.
    DepthLimit = 6,

    // Chamada HTTP cuja rota é montada em tempo de execução: o extrator não sabe para onde vai.
    RouteNotResolved = 7,

    // A rota resolveu, mas nenhum repositório escolhido para seguir as chamadas a atende.
    NoMatchingEndpoint = 8,

    // A rota casa com mais de um destino: o extrator não escolhe sozinho.
    MultipleEndpoints = 9
}
