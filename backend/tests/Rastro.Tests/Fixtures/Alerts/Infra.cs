// Fixture do extrator: código de exemplo com os casos que o Rastro precisa tratar.
// Não é compilado com os testes; é carregado num workspace em memória.
namespace Fixture.Alerts;

public interface IServiceCollection { }

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services)
        where TImplementation : TService => services;
}

public static class Registrations
{
    public static void Register(IServiceCollection services)
    {
        services.AddScoped<IAlertSender, TelegramAlertSender>();
        // IFormatter tem duas implementações; o DI escolhe a PlainFormatter.
        services.AddScoped<IFormatter, PlainFormatter>();
    }
}

public interface IMediator
{
    Task Send(object request);
}

public record AlertsProcessed;

public class AlertFailedException(int id, Exception inner) : Exception($"Alerta {id}", inner);

public record Alert(int Id, bool Muted, string Text);
