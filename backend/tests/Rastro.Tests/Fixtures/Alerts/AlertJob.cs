namespace Fixture.Alerts;

public class AlertJob(
    IAlertSender sender,
    IFormatter formatter,
    INotifier notifier,
    IOrphan orphan,
    IMediator mediator,
    Func<int, bool> filter, IClock clock)
{
    public async Task ExecuteAsync(List<Alert> alerts)
    {
        var due = LoadDue(alerts);
        var now = clock.Now();

        foreach (var alert in due)
        {
            if (alert.Muted)
            {
                continue;
            }

            if (alert.Id < 0) break;

            try
            {
                var text = formatter.Format(alert);
                await sender.SendAsync(text);
            }
            catch (InvalidOperationException ex)
            {
                notifier.Notify();
                throw new AlertFailedException(alert.Id, ex);
            }
        }

        switch (due.Count)
        {
            case 0:
                orphan.Run();
                break;
            default:
                Log();
                break;
        }

        if (filter(due.Count))
        {
            return;
        }

        await mediator.Send(new AlertsProcessed());
        Recurse(3);
    }

    private List<Alert> LoadDue(List<Alert> alerts) => alerts.Where(a => IsDue(a)).ToList();

    private static bool IsDue(Alert alert) => !alert.Muted;

    private void Log() { }

    private void Recurse(int n)
    {
        if (n > 0) Recurse(n - 1);
    }
}
