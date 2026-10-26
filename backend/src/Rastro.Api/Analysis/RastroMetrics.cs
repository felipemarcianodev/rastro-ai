using System.Diagnostics.Metrics;
using Rastro.Core.Validation;

namespace Rastro.Api.Analysis;

// Métricas da análise. Labels de baixa cardinalidade (nunca id de levantamento ou repositório).
public sealed class RastroMetrics : IDisposable
{
    public const string MeterName = "Rastro.Api";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _surveys;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _validations;

    public RastroMetrics()
    {
        _surveys = _meter.CreateCounter<long>("rastro_surveys_total", description: "Levantamentos processados, por resultado.");
        _duration = _meter.CreateHistogram<double>("rastro_survey_duration_seconds", unit: "s", description: "Duração da análise de um levantamento.");
        _validations = _meter.CreateCounter<long>("rastro_validations_total", description: "Validações de mudança, por veredito (failed = não chegou ao fim).");
    }

    public void Completed(TimeSpan duration)
    {
        _surveys.Add(1, new KeyValuePair<string, object?>("result", "completed"));
        _duration.Record(duration.TotalSeconds);
    }

    public void Failed(string reason) =>
        _surveys.Add(1, new KeyValuePair<string, object?>("result", "failed"), new KeyValuePair<string, object?>("reason", reason));

    public void Validated(ValidationVerdict? verdict) =>
        _validations.Add(1, new KeyValuePair<string, object?>("result", verdict switch
        {
            ValidationVerdict.Approved => "approved",
            ValidationVerdict.Partial => "partial",
            ValidationVerdict.Rejected => "rejected",
            _ => "failed"
        }));

    public void Dispose() => _meter.Dispose();
}
