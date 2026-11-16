using System.Globalization;
using System.Text.Json;

namespace Rastro.Core.Failures;

// Texto em português de cada código de falha: o que vai para o log, para o MCP (até a Fase 51) e
// para a coluna de texto antiga. A tela traduz pelo código e pelos parâmetros, nos JSON de tradução.
public static class FailureText
{
    private static readonly Dictionary<SurveyFailure, string> Survey = new()
    {
        [SurveyFailure.Unexpected] = "Falha inesperada na análise. Detalhes no log da API.",
        [SurveyFailure.Interrupted] = "Análise interrompida: a API reiniciou durante o processamento.",
        [SurveyFailure.TargetUnavailable] = "O repositório ou a solution não estão mais disponíveis na configuração.",
        [SurveyFailure.EntryInvalid] = "Ponto de entrada inválido: \"{0}\". Use Tipo.Metodo ou Namespace.Tipo.Metodo.",
        [SurveyFailure.EntryNotFound] = "Método \"{0}\" não encontrado na solution: nenhum método chamado {1}, com corpo, existe nela.",
        [SurveyFailure.EntryNotFoundSuggest] = "Método \"{0}\" não encontrado na solution. Com o nome {1} existe: {2}. Use Tipo.Metodo ou Namespace.Tipo.Metodo.",
        [SurveyFailure.EntryNotFoundSuggestMore] = "Método \"{0}\" não encontrado na solution. Com o nome {1} existe: {2} e mais {3}. Use Tipo.Metodo ou Namespace.Tipo.Metodo.",
        [SurveyFailure.EntryAmbiguous] = "\"{0}\" é ambíguo. Use o nome completo: {1}.",
        [SurveyFailure.AngularScriptMissing] = "O extrator Angular não veio no pacote desta instalação. Reinstale o Rastro.",
        [SurveyFailure.AngularTimeout] = "A leitura do Angular passou de {0} s. Aumente Rastro:Analysis:TypeScriptTimeoutSeconds ou reduza a profundidade.",
        [SurveyFailure.NodeMissing] = "O Node não foi encontrado nesta máquina. Instale o Node para levantar fluxos Angular.",
        [SurveyFailure.AngularUnreadable] = "Não foi possível ler o código Angular. Veja o log da API para o detalhe.",
        [SurveyFailure.AngularScriptError] = "{0}",
        [SurveyFailure.RestoreFailed] = "O dotnet restore da solution falhou. Confira se ela compila na máquina."
    };

    private static readonly Dictionary<ValidationFailure, string> Validation = new()
    {
        [ValidationFailure.Unexpected] = "Falha inesperada na validação. Detalhes no log.",
        [ValidationFailure.Interrupted] = "Validação interrompida: a API reiniciou durante o processamento.",
        [ValidationFailure.DraftDeleted] = "O rascunho foi apagado antes da validação.",
        [ValidationFailure.TargetUnavailable] = "O repositório ou a solution não estão mais disponíveis na configuração.",
        [ValidationFailure.AngularUnsupported] = "A validação de mudança ainda não lê código Angular.",
        [ValidationFailure.EntryGone] = "O ponto de entrada \"{0}\" não existe mais no código."
    };

    private static readonly Dictionary<ProposalRefusal, string> Refusal = new()
    {
        [ProposalRefusal.SurveyNotFound] = "Levantamento não encontrado ou ainda sem grafo.",
        [ProposalRefusal.NodeNotFound] = "Nó não encontrado neste levantamento.",
        [ProposalRefusal.RuleLength] = "A regra precisa ter de {0} a {1} caracteres.",
        [ProposalRefusal.NotUnresolved] = "Só nós não resolvidos recebem proposta de destino; este já foi resolvido pelo compilador.",
        [ProposalRefusal.TargetUnavailable] = "O repositório ou a solution não estão mais disponíveis na configuração.",
        [ProposalRefusal.AngularUnsupported] = "A prova de proposta ainda não lê código Angular: só nós .NET recebem proposta de destino.",
        [ProposalRefusal.CodeChanged] = "O código mudou desde este levantamento. Levante o fluxo de novo antes de propor.",
        [ProposalRefusal.DuplicateTarget] = "Esse destino já foi proposto para este nó. Para trocar, descarte o atual na tela.",
        [ProposalRefusal.HttpNeedsSystemNoCross] = "Este nó é uma chamada HTTP sem destino. Informe o repositório de destino (system); este levantamento não segue chamadas HTTP para outros repositórios.",
        [ProposalRefusal.HttpNeedsSystem] = "Este nó é uma chamada HTTP sem destino. Informe o repositório de destino (system); repositórios da costura: {0}.",
        [ProposalRefusal.HttpCallMissing] = "A chamada HTTP deste nó não está registrada no grafo; levante o fluxo de novo.",
        [ProposalRefusal.DestinationUnavailable] = "O repositório {0} não está disponível ou não tem solution na configuração.",
        [ProposalRefusal.DestinationCodeChanged] = "O código de {0} mudou desde este levantamento. Levante o fluxo de novo antes de propor.",
        [ProposalRefusal.ProofIncomplete] = "Prova incompleta: informe o método de destino, o arquivo e a linha.",
        [ProposalRefusal.ProofNoBody] = "{0} não tem corpo no código (abstrato, externo ou gerado).",
        [ProposalRefusal.ProofWrongFile] = "A prova não confere: {0} está em {1}:{2}, não em {3}.",
        [ProposalRefusal.ProofLineOutside] = "A prova não confere: {0} vai da linha {1} à {2} de {3}; a linha {4} está fora.",
        [ProposalRefusal.RouteNotIndexed] = "{0} não atende nenhuma rota HTTP do repositório (não está no índice de rotas).",
        [ProposalRefusal.VerbMismatch] = "{0} não atende {1}: o verbo da chamada não confere com o do destino.",
        [ProposalRefusal.RouteMismatch] = "{0} atende {1}, que não casa com a rota chamada ({2} {3})."
    };

    private static readonly Dictionary<DraftError, string> Draft = new()
    {
        [DraftError.EntryChanged] = "O rascunho precisa manter a mesma entrada do levantamento.",
        [DraftError.DuplicateNodes] = "Nós repetidos no rascunho: {0}.",
        [DraftError.CodeStepMissing] = "O passo \"{0}\" do código sumiu do rascunho. Para tirá-lo, marque como removido.",
        [DraftError.CodeStepAltered] = "O passo \"{0}\" do código foi alterado. Só dá para marcá-lo como removido ou modificado.",
        [DraftError.CodeStepMarkedNew] = "O passo \"{0}\" já existe no código; não pode ser marcado como novo.",
        [DraftError.CodeStepModifiedNoIntent] = "O passo \"{0}\" foi marcado como modificado sem dizer o que muda.",
        [DraftError.TooManyAdded] = "No máximo {0} passos novos por rascunho.",
        [DraftError.NewStepBadId] = "O passo novo \"{0}\" precisa de id iniciado por \"{1}\" e marca de novo.",
        [DraftError.NewStepNoName] = "Passo novo sem nome (ou com mais de {0} caracteres).",
        [DraftError.NewStepNoIntent] = "O passo novo \"{0}\" precisa dizer o que deve fazer.",
        [DraftError.NewStepBadKind] = "O passo novo \"{0}\" só pode ser um método ou uma decisão.",
        [DraftError.IntentTooLong] = "A intenção de \"{0}\" passa de {1} caracteres.",
        [DraftError.EdgeMissing] = "Uma ligação do código ({0} → {1}) sumiu. Para tirá-la, marque como removida.",
        [DraftError.EdgeMarkedNew] = "A ligação {0} → {1} já existe no código; não pode ser marcada como nova.",
        [DraftError.EdgeDangling] = "Uma ligação nova aponta para um passo que não está no rascunho.",
        [DraftError.ForeignSystem] = "Edição só no primeiro sistema do fluxo."
    };

    public static string Of(SurveyFailure code, IReadOnlyList<string>? args = null) => Format(Survey[code], args);

    public static string Of(ValidationFailure code, IReadOnlyList<string>? args = null) =>
        code == ValidationFailure.Analysis ? Nested(args ?? []) : Format(Validation[code], args);

    public static string Of(ProposalRefusal code, IReadOnlyList<string>? args = null) => code switch
    {
        ProposalRefusal.AnalysisFailed => Nested(args ?? []),
        ProposalRefusal.ProofTargetInvalid => $"O método de destino não confere: {Nested(args ?? [])}",
        _ => Format(Refusal[code], args)
    };

    public static string Of(DraftError code, IReadOnlyList<string>? args = null) => Format(Draft[code], args);

    // Código aninhado: o primeiro parâmetro é o código do SurveyFailure e o resto são os parâmetros dele.
    public static IReadOnlyList<string> Nest(SurveyFailure code, IReadOnlyList<string>? args) =>
        [((int)code).ToString(CultureInfo.InvariantCulture), .. args ?? []];

    public static string Nested(IReadOnlyList<string> args)
    {
        if (args.Count > 0 && int.TryParse(args[0], CultureInfo.InvariantCulture, out var code) && Enum.IsDefined((SurveyFailure)code))
            return Of((SurveyFailure)code, args.Skip(1).ToList());
        return string.Empty;
    }

    public static string SerializeArgs(IReadOnlyList<string>? args) =>
        args is { Count: > 0 } ? JsonSerializer.Serialize(args) : "[]";

    public static IReadOnlyList<string> ParseArgs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            // Coluna gravada por outra versão ou editada à mão: sem parâmetros, a tela cai no texto antigo.
            return [];
        }
    }

    private static string Format(string template, IReadOnlyList<string>? args) =>
        string.Format(CultureInfo.InvariantCulture, template, (args ?? []).Cast<object>().ToArray());
}
