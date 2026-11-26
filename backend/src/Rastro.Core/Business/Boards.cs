namespace Rastro.Core.Business;

// Quadro de fluxos (Fase 60): vários levantamentos lado a lado, uma faixa por levantamento. Um levantamento pode estar
// em vários quadros. O quadro guarda só nome, glossário e a ordem das faixas; a visão de negócio de cada faixa é a do
// levantamento (rótulos e tipos são por levantamento) e a posição dos balões é por quadro (LayoutScope.Board).
public class Board
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    // Texto simples, uma entrada por linha ("Termo: significado"); vai à IA no "Sugerir textos". Nulo quando não há.
    public string? Glossary { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Board() { }

    public static Board Create(string name, string? glossary, DateTimeOffset now) =>
        new() { Id = Guid.NewGuid(), Name = name, Glossary = glossary, CreatedAt = now, UpdatedAt = now };

    public void Update(string name, string? glossary, DateTimeOffset now)
    {
        Name = name;
        Glossary = glossary;
        UpdatedAt = now;
    }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}

// Faixa do quadro. Chave (quadro, levantamento). Excluir o levantamento ou o quadro tira a faixa (cascata).
public class BoardLane
{
    public Guid BoardId { get; private set; }
    public Guid SurveyId { get; private set; }
    public int Order { get; private set; }
    public string Title { get; private set; } = string.Empty;

    private BoardLane() { }

    public static BoardLane Create(Guid boardId, Guid surveyId, int order, string title) =>
        new() { BoardId = boardId, SurveyId = surveyId, Order = order, Title = title };

    public void Set(int order, string title)
    {
        Order = order;
        Title = title;
    }
}

// Limites do que o usuário escreve no quadro.
public static class BoardLimits
{
    public const int MaxNameLength = 120;
    public const int MaxTitleLength = 120;
    public const int MaxGlossaryLength = 8000;
    public const int MaxLanes = 12;

    // Uma linha, sem caractere de controle. Nulo quando não vale.
    public static string? CleanName(string? text)
    {
        var clean = (text ?? string.Empty).Trim();
        return clean.Length is 0 or > MaxNameLength || clean.Any(char.IsControl) ? null : clean;
    }

    // Título da faixa: vazio é aceito (a faixa usa o ponto de entrada); nulo quando excede o limite ou tem controle.
    public static string? CleanTitle(string? text)
    {
        var clean = (text ?? string.Empty).Trim();
        return clean.Length > MaxTitleLength || clean.Any(char.IsControl) ? null : clean;
    }

    // Texto simples com quebras de linha e tabulação; vazio vira nulo. O segundo valor é falso quando não vale.
    public static (string? Text, bool Valid) CleanGlossary(string? text)
    {
        var clean = (text ?? string.Empty).Trim();
        if (clean.Length == 0) return (null, true);
        if (clean.Length > MaxGlossaryLength) return (null, false);
        return clean.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) ? (null, false) : (clean, true);
    }
}

// Posição de um passo no quadro: o id do passo é por levantamento, então leva o levantamento da faixa.
public record BoardStepPositionDto(Guid LaneSurveyId, string StepId, double X, double Y);

public record BoardEdgePointsDto(Guid LaneSurveyId, string From, string To, IReadOnlyList<LayoutPoint> Points);

// Formato da rota de layout do quadro (GET dentro do quadro, PUT), em camelCase no JSON.
public record BoardLayoutDto(IReadOnlyList<BoardStepPositionDto> Steps, IReadOnlyList<BoardEdgePointsDto> Edges);
