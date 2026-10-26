using Microsoft.EntityFrameworkCore;
using Rastro.Core.Validation;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Validation;

namespace Rastro.Api.Analysis;

// Processa a fila de validações. Falha de uma validação fica registrada nela (Failed com a
// mensagem) e nunca derruba o worker.
public class ValidationWorker(
    ValidationQueue queue,
    IServiceScopeFactory scopes,
    RastroMetrics metrics,
    TimeProvider clock,
    ILogger<ValidationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);

        await foreach (var validationId in queue.ReadAllAsync(stoppingToken))
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ChangeValidationService>().RunAsync(validationId, stoppingToken);

            var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
            var done = await db.SurveyValidations.AsNoTracking().FirstOrDefaultAsync(v => v.Id == validationId, stoppingToken);
            metrics.Validated(done?.ReadResult()?.Verdict);
        }
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();

        var interrupted = await db.SurveyValidations.Where(v => v.Status == ValidationStatus.Running).ToListAsync(ct);
        foreach (var validation in interrupted)
            validation.Fail("Validação interrompida: a API reiniciou durante o processamento.", clock.GetUtcNow());
        if (interrupted.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogWarning("{Count} validação(ões) interrompida(s) marcadas como falha ao subir.", interrupted.Count);
        }

        var pending = await db.SurveyValidations.Where(v => v.Status == ValidationStatus.Pending).OrderBy(v => v.CreatedAt).Select(v => v.Id).ToListAsync(ct);
        foreach (var id in pending)
            await queue.EnqueueAsync(id, ct);
    }
}
