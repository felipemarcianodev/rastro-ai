namespace Fixture.Alerts.Tests;

// Fake de teste: projeto "Alerts.Tests" (detectado como projeto de teste pelo nome).
public class FakeClock : Fixture.Alerts.IClock
{
    public DateTime Now() => new(2026, 1, 1);
}
