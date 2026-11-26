using Microsoft.Extensions.Configuration;
using Rastro.Core.Business;
using Rastro.Infrastructure.Configuration;

namespace Rastro.Tests.Infrastructure;

// Cenários INF-BIZ-1NN: as opções da visão de negócio no config.json (Rastro:BusinessView, Fase 55).
public class BusinessViewSettingsTests
{
    private static BusinessViewOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new RastroOptions();
        configuration.GetSection(RastroOptions.Section).Bind(options);
        return options.BusinessView.ToOptions();
    }

    [Fact] // INF-BIZ-101
    public void ToOptions_SemSecaoNoConfig_UsaOsPadroesDoNucleo()
    {
        var options = Bind([]);

        Assert.Equal(BusinessViewOptions.Default.NotificationPatterns, options.NotificationPatterns);
        Assert.Equal(BusinessViewOptions.Default.RejectionReturnTypes, options.RejectionReturnTypes);
        Assert.Equal(BusinessViewOptions.DefaultMaxStepsPerFlow, options.MaxStepsPerFlow);
    }

    [Fact] // INF-BIZ-102
    public void ToOptions_ListasNoConfig_TrocamOPadraoEmVezDeSomar()
    {
        var options = Bind(new()
        {
            ["Rastro:BusinessView:NotificationPatterns:0"] = "*Aviso*.Enviar*",
            ["Rastro:BusinessView:RejectionReturnTypes:0"] = "Recusado",
            ["Rastro:BusinessView:MaxStepsPerFlow"] = "12"
        });

        Assert.Equal(["*Aviso*.Enviar*"], options.NotificationPatterns);
        Assert.Equal(["Recusado"], options.RejectionReturnTypes);
        Assert.Equal(12, options.MaxStepsPerFlow);
    }

    [Fact] // INF-BIZ-103
    public void ToOptions_LimiteZeroOuNegativo_VoltaAoPadrao()
    {
        var options = Bind(new() { ["Rastro:BusinessView:MaxStepsPerFlow"] = "0" });

        Assert.Equal(BusinessViewOptions.DefaultMaxStepsPerFlow, options.MaxStepsPerFlow);
    }
}
