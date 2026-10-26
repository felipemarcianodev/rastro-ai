using System.Text.Json;
using Rastro.Core.Architecture;
using Rastro.Core.Graph;

namespace Rastro.Core.Surveys;

// O que o C4 não tira do código e o usuário completa na tela (Fase 11): as pessoas do C1.
// Fica salvo junto com o levantamento; um registro por levantamento.
public class SurveyArchitecture
{
    public const int MaxPeople = 10;
    public const int MaxNameLength = 80;
    public const int MaxDescriptionLength = 200;

    public Guid SurveyId { get; private set; }
    public string PeopleJson { get; private set; } = "[]";
    public DateTimeOffset UpdatedAt { get; private set; }

    private SurveyArchitecture() { }

    public static SurveyArchitecture Create(Guid surveyId, IReadOnlyList<C4Person> people, DateTimeOffset now)
    {
        var architecture = new SurveyArchitecture { SurveyId = surveyId };
        architecture.Update(people, now);
        return architecture;
    }

    public void Update(IReadOnlyList<C4Person> people, DateTimeOffset now)
    {
        PeopleJson = JsonSerializer.Serialize(people, FlowGraphJson.Options);
        UpdatedAt = now;
    }

    public IReadOnlyList<C4Person> ReadPeople() =>
        JsonSerializer.Deserialize<List<C4Person>>(PeopleJson, FlowGraphJson.Options) ?? [];

    // Nome obrigatório, sem repetir; descrição opcional. Devolve os problemas em linguagem do usuário.
    public static IReadOnlyList<string> Validate(IReadOnlyList<C4Person> people)
    {
        var errors = new List<string>();
        if (people.Count > MaxPeople) errors.Add($"No máximo {MaxPeople} pessoas.");
        foreach (var person in people)
        {
            if (string.IsNullOrWhiteSpace(person.Name) || person.Name.Length > MaxNameLength)
                errors.Add($"Cada pessoa precisa de um nome com até {MaxNameLength} caracteres.");
            if (person.Description is { Length: > MaxDescriptionLength })
                errors.Add($"A descrição de \"{person.Name}\" passa de {MaxDescriptionLength} caracteres.");
        }
        if (people.GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            errors.Add("Há pessoas com o mesmo nome.");
        return errors.Distinct().ToList();
    }
}
