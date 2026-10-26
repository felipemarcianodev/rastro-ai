using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Rastro.Ai.Settings;

// Usos da IA, cada um ligado por uma flag na tela.
public enum AiUse
{
    FillHoles = 0,
    DescribeRules = 1,
    Suggestions = 2,
    ContextSuggestions = 3
}

// O que a tela pode saber da configuração: nunca a chave, só se ela existe.
public record AiSettingsView(
    bool Enabled,
    AiProvider Provider,
    string ClaudeModel,
    bool ClaudeHasKey,
    string OpenAiModel,
    bool OpenAiHasKey,
    string GeminiModel,
    bool GeminiHasKey,
    int DailyTokenBudget,
    bool UseFillHoles,
    bool UseDescribeRules,
    bool UseSuggestions,
    bool UseContextSuggestions,
    DateTimeOffset UpdatedAt);

// Atualização vinda da tela. Chave nula = manter a atual; vazia = apagar.
public record AiSettingsUpdate(
    bool Enabled,
    AiProvider Provider,
    string? ClaudeModel,
    string? ClaudeKey,
    string? OpenAiModel,
    string? OpenAiKey,
    string? GeminiModel,
    string? GeminiKey,
    int DailyTokenBudget,
    bool UseFillHoles,
    bool UseDescribeRules,
    bool UseSuggestions,
    bool UseContextSuggestions);

public class AiSettingsService(AiDbContext db, IDataProtectionProvider protection, TimeProvider clock)
{
    private const string Purpose = "Rastro.Ai.ProviderKeys";
    public const int MaxModelLength = 100;
    public const int MaxKeyLength = 400;

    private IDataProtector Protector => protection.CreateProtector(Purpose);

    public async Task<AiSettings> GetAsync(CancellationToken ct) =>
        await db.Settings.FirstOrDefaultAsync(s => s.Id == AiSettings.SingletonId, ct)
        ?? new AiSettings { UpdatedAt = clock.GetUtcNow() };

    public static AiSettingsView View(AiSettings s) => new(
        s.Enabled, s.Provider,
        s.ClaudeModel, !string.IsNullOrEmpty(s.ClaudeKeyProtected),
        s.OpenAiModel, !string.IsNullOrEmpty(s.OpenAiKeyProtected),
        s.GeminiModel, !string.IsNullOrEmpty(s.GeminiKeyProtected),
        s.DailyTokenBudget, s.UseFillHoles, s.UseDescribeRules, s.UseSuggestions, s.UseContextSuggestions, s.UpdatedAt);

    public async Task<AiSettings> UpdateAsync(AiSettingsUpdate update, CancellationToken ct)
    {
        if (!Enum.IsDefined(update.Provider)) throw new ArgumentException("Provedor desconhecido.");
        if (update.DailyTokenBudget < 0) throw new ArgumentException("O orçamento diário não pode ser negativo.");
        foreach (var model in new[] { update.ClaudeModel, update.OpenAiModel, update.GeminiModel })
            if (model is not null && (model.Trim().Length == 0 || model.Length > MaxModelLength))
                throw new ArgumentException($"Nome de modelo precisa ter de 1 a {MaxModelLength} caracteres.");
        foreach (var key in new[] { update.ClaudeKey, update.OpenAiKey, update.GeminiKey })
            if (key is { Length: > MaxKeyLength })
                throw new ArgumentException("Chave de API longa demais.");

        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == AiSettings.SingletonId, ct);
        if (settings is null)
        {
            settings = new AiSettings();
            db.Settings.Add(settings);
        }

        settings.Enabled = update.Enabled;
        settings.Provider = update.Provider;
        settings.ClaudeModel = update.ClaudeModel?.Trim() ?? settings.ClaudeModel;
        settings.OpenAiModel = update.OpenAiModel?.Trim() ?? settings.OpenAiModel;
        settings.GeminiModel = update.GeminiModel?.Trim() ?? settings.GeminiModel;
        settings.ClaudeKeyProtected = Protect(update.ClaudeKey, settings.ClaudeKeyProtected);
        settings.OpenAiKeyProtected = Protect(update.OpenAiKey, settings.OpenAiKeyProtected);
        settings.GeminiKeyProtected = Protect(update.GeminiKey, settings.GeminiKeyProtected);
        settings.DailyTokenBudget = update.DailyTokenBudget;
        settings.UseFillHoles = update.UseFillHoles;
        settings.UseDescribeRules = update.UseDescribeRules;
        settings.UseSuggestions = update.UseSuggestions;
        settings.UseContextSuggestions = update.UseContextSuggestions;
        settings.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(ct);
        return settings;
    }

    public string? Unprotect(string? protectedKey) =>
        string.IsNullOrEmpty(protectedKey) ? null : Protector.Unprotect(protectedKey);

    private string? Protect(string? newKey, string? current) => newKey switch
    {
        null => current,
        "" => null,
        _ => Protector.Protect(newKey.Trim())
    };
}
