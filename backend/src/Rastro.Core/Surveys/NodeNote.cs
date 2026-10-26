namespace Rastro.Core.Surveys;

// Anotação do usuário num passo (Fase 12): "cuidado: chamado também pelo webhook". Fica por
// repositório e passo, não por levantamento: o id do passo vem do símbolo, então a nota aparece
// de novo no levantamento seguinte e em outro fluxo que passe pelo mesmo método.
public class NodeNote
{
    public const int MaxTextLength = 500;

    public Guid Id { get; private set; }
    public string Repository { get; private set; } = string.Empty;
    public string NodeId { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    private NodeNote() { }

    public static NodeNote Create(string repository, string nodeId, string text, DateTimeOffset now) =>
        new() { Id = Guid.NewGuid(), Repository = repository, NodeId = nodeId, Text = text, UpdatedAt = now };

    public void Update(string text, DateTimeOffset now)
    {
        Text = text;
        UpdatedAt = now;
    }

    // Texto vazio apaga a nota; acima do limite é recusado. Devolve o problema para o usuário.
    public static string? Validate(string text) =>
        text.Length > MaxTextLength ? $"A anotação passa de {MaxTextLength} caracteres." : null;
}
