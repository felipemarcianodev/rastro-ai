namespace Fixture.NegocioPagamentos;

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
        services.AddScoped<IEmailService, EmailService>();
    }
}

public interface IEmailService
{
    Task SendAsync(string destinatario, string assunto);
}

public class EmailService : IEmailService
{
    public Task SendAsync(string destinatario, string assunto) => Task.CompletedTask;
}

public class EstornosService(IEmailService email)
{
    // No cartão, avisa o cliente por e-mail; nos outros meios o estorno termina sem notificar.
    public async Task EstornarAsync(EstornoRequest request)
    {
        if (request.Meio == "cartao")
            await email.SendAsync("cliente@exemplo.com", "Confirmação do estorno");
    }
}
