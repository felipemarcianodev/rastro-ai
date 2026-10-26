using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rastro.Ai.Providers;
using Rastro.Ai.Settings;

namespace Rastro.Ai;

public enum AiBlock
{
    Disabled = 0,
    UseDisabled = 1,
    NotConfigured = 2,
    BudgetExceeded = 3
}

public class AiBlockedException(AiBlock reason, string message) : Exception(message)
{
    public AiBlock Reason { get; } = reason;
}

public record AiStatus(bool Enabled, bool Configured, AiProvider Provider, string Model, IReadOnlyList<AiUse> Uses,
    long TokensToday, int DailyTokenBudget);

// Porta única para chamar o provedor: confere flag geral, flag do uso, chave e orçamento
// diário antes de gastar, e registra o uso depois. Falha nunca derruba quem chamou além do
// próprio pedido (vira AiProviderException ou AiBlockedException com mensagem para a tela).
public class AiGateway(
    AiDbContext db,
    AiSettingsService settings,
    IEnumerable<IAiTextService> services,
    TimeProvider clock,
    ILogger<AiGateway> logger)
{
    private static readonly Meter Meter = new("Rastro.Ai");
    private static readonly Counter<long> Calls = Meter.CreateCounter<long>("rastro_ai_calls_total", description: "Chamadas ao provedor de IA, por provedor, uso e resultado.");
    private static readonly Counter<long> Tokens = Meter.CreateCounter<long>("rastro_ai_tokens_total", description: "Tokens gastos, por provedor e direção.");

    public async Task<AiStatus> StatusAsync(CancellationToken ct)
    {
        var current = await settings.GetAsync(ct);
        var uses = new List<AiUse>();
        if (current.UseFillHoles) uses.Add(AiUse.FillHoles);
        if (current.UseDescribeRules) uses.Add(AiUse.DescribeRules);
        if (current.UseSuggestions) uses.Add(AiUse.Suggestions);
        if (current.UseContextSuggestions) uses.Add(AiUse.ContextSuggestions);
        var today = await TodayAsync(ct);

        return new AiStatus(current.Enabled, !string.IsNullOrEmpty(current.KeyProtectedFor(current.Provider)), current.Provider,
            current.ModelFor(current.Provider), uses, (today?.InputTokens ?? 0) + (today?.OutputTokens ?? 0), current.DailyTokenBudget);
    }

    // Confere flag geral, flag do uso e chave. Quem usa a IA chama antes de qualquer trabalho
    // (inclusive antes de reaproveitar cache): uso desligado não produz nada.
    public async Task EnsureAllowedAsync(AiUse use, CancellationToken ct) => await AllowedAsync(use, ct);

    private async Task<(AiSettings Settings, string Key)> AllowedAsync(AiUse use, CancellationToken ct)
    {
        var current = await settings.GetAsync(ct);
        if (!current.Enabled) throw new AiBlockedException(AiBlock.Disabled, "A IA está desligada na configuração.");
        if (!UseEnabled(current, use)) throw new AiBlockedException(AiBlock.UseDisabled, "Este uso da IA está desligado na configuração.");

        var key = settings.Unprotect(current.KeyProtectedFor(current.Provider));
        if (key is null) throw new AiBlockedException(AiBlock.NotConfigured, "Falta a chave de API do provedor escolhido.");
        return (current, key);
    }

    public async Task<AiResult> GenerateAsync(AiUse use, AiRequest request, CancellationToken ct)
    {
        var (current, key) = await AllowedAsync(use, ct);

        var today = await TodayAsync(ct);
        if (current.DailyTokenBudget > 0 && (today?.InputTokens ?? 0) + (today?.OutputTokens ?? 0) >= current.DailyTokenBudget)
            throw new AiBlockedException(AiBlock.BudgetExceeded, "O orçamento diário de tokens acabou. Ele volta amanhã (UTC) ou pode ser aumentado na configuração.");

        var service = services.FirstOrDefault(s => s.Provider == current.Provider)
            ?? throw new AiBlockedException(AiBlock.NotConfigured, "Provedor sem implementação.");

        var provider = current.Provider.ToString();
        try
        {
            var result = await service.GenerateJsonAsync(request, key, current.ModelFor(current.Provider), ct);
            await RecordAsync(result, ct);
            Calls.Add(1, Tag("provider", provider), Tag("use", use.ToString()), Tag("result", "ok"));
            Tokens.Add(result.InputTokens, Tag("provider", provider), Tag("direction", "input"));
            Tokens.Add(result.OutputTokens, Tag("provider", provider), Tag("direction", "output"));
            logger.LogInformation("IA {Provider} ({Use}): {InputTokens} tokens de entrada, {OutputTokens} de saída; pedido {PromptHash}.",
                provider, use, result.InputTokens, result.OutputTokens, Hash(request.User));
            return result;
        }
        catch (AiProviderException ex)
        {
            Calls.Add(1, Tag("provider", provider), Tag("use", use.ToString()), Tag("result", "error"));
            logger.LogWarning(ex, "IA {Provider} ({Use}) falhou.", provider, use);
            throw;
        }
    }

    private static bool UseEnabled(AiSettings s, AiUse use) => use switch
    {
        AiUse.FillHoles => s.UseFillHoles,
        AiUse.DescribeRules => s.UseDescribeRules,
        AiUse.Suggestions => s.UseSuggestions,
        AiUse.ContextSuggestions => s.UseContextSuggestions,
        _ => false
    };

    private Task<AiUsageDay?> TodayAsync(CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return db.UsageDays.FirstOrDefaultAsync(u => u.Day == day, ct);
    }

    private async Task RecordAsync(AiResult result, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var usage = await db.UsageDays.FirstOrDefaultAsync(u => u.Day == day, ct);
        if (usage is null)
        {
            usage = new AiUsageDay { Day = day };
            db.UsageDays.Add(usage);
        }
        usage.InputTokens += result.InputTokens;
        usage.OutputTokens += result.OutputTokens;
        usage.Calls++;
        await db.SaveChangesAsync(ct);
    }

    // Log nunca leva o prompt (tem código do cliente): só um hash curto para correlacionar.
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..12];

    private static KeyValuePair<string, object?> Tag(string key, string value) => new(key, value);
}
