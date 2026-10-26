namespace Fixture.Alerts;

public interface IAlertSender
{
    Task SendAsync(string message);
}

public class TelegramAlertSender(HttpClient http) : IAlertSender
{
    private readonly HttpClient _http = http;

    public async Task SendAsync(string message)
    {
        await _http.PostAsync("https://api.telegram.org/send", new StringContent(message));
    }
}

public interface IFormatter
{
    string Format(Alert alert);
}

public class PlainFormatter : IFormatter
{
    public string Format(Alert alert) => alert.Text.Trim();
}

public class HtmlFormatter : IFormatter
{
    public string Format(Alert alert) => $"<b>{alert.Text}</b>";
}

// Duas implementações e nenhum registro de DI: o extrator não sabe qual roda.
public interface INotifier
{
    void Notify();
}

public class EmailNotifier : INotifier
{
    public void Notify() { }
}

public class SmsNotifier : INotifier
{
    public void Notify() { }
}

// Nenhuma implementação no código.
public interface IOrphan
{
    void Run();
}

// Uma implementação de produção; o projeto de teste tem um fake que não pode competir com ela.
public interface IClock
{
    DateTime Now();
}

public class SystemClock : IClock
{
    public DateTime Now() => DateTime.UtcNow;
}
