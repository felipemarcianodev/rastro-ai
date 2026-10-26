namespace Rastro.Core.Graph;

// Marca de mudança num rascunho de edição (Fase 7). O grafo extraído nunca tem marca: ele é o
// que o código faz; o rascunho é o que o usuário quer que ele passe a fazer.
public enum ChangeKind
{
    // Passo ou ligação que não existe no código e deve passar a existir.
    Added = 0,

    // Passo ou ligação do código que deve deixar de existir.
    Removed = 1,

    // Passo do código que continua, mas com outra regra (a intenção diz qual).
    Modified = 2
}
