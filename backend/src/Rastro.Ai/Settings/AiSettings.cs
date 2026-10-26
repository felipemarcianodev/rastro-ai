namespace Rastro.Ai.Settings;

// Provedor usado no modo provedor (ADR-001). Valores explícitos: viajam como int no JSON.
public enum AiProvider
{
    Claude = 0,
    OpenAi = 1,
    Gemini = 2
}

// Configuração da IA no estilo feature flags (linha única, Id = 1), no padrão do meu-portfolio:
// liga/desliga geral, provedor ativo, chave CIFRADA e modelo por provedor, orçamento diário e
// uma flag por uso. Chave nunca em texto plano, nunca devolvida pela API, nunca em log.
public class AiSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public bool Enabled { get; set; }
    public AiProvider Provider { get; set; } = AiProvider.Claude;

    public string? ClaudeKeyProtected { get; set; }
    public string ClaudeModel { get; set; } = "claude-opus-5-5";
    public string? OpenAiKeyProtected { get; set; }
    public string OpenAiModel { get; set; } = "gpt-4o";
    public string? GeminiKeyProtected { get; set; }
    public string GeminiModel { get; set; } = "gemini-2.5-flash";

    // Tokens (entrada + saída) por dia UTC; 0 = sem limite.
    public int DailyTokenBudget { get; set; } = 200_000;

    // Usos da IA, cada um com a sua flag.
    public bool UseFillHoles { get; set; } = true;
    public bool UseDescribeRules { get; set; } = true;
    public bool UseSuggestions { get; set; } = true;
    public bool UseContextSuggestions { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; }

    public string? KeyProtectedFor(AiProvider provider) => provider switch
    {
        AiProvider.Claude => ClaudeKeyProtected,
        AiProvider.OpenAi => OpenAiKeyProtected,
        AiProvider.Gemini => GeminiKeyProtected,
        _ => null
    };

    public string ModelFor(AiProvider provider) => provider switch
    {
        AiProvider.Claude => ClaudeModel,
        AiProvider.OpenAi => OpenAiModel,
        AiProvider.Gemini => GeminiModel,
        _ => string.Empty
    };
}

// Uso de tokens por dia UTC (para o orçamento diário).
public class AiUsageDay
{
    public DateOnly Day { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public int Calls { get; set; }
}
