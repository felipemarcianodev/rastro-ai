using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rastro.Core;
using Rastro.Core.Business;
using Rastro.Core.Graph;
using Rastro.Core.Localization;

namespace Rastro.Tests.Core;

// Cenários CORE-BIZ-NNN: a derivação dos passos-chave da visão de negócio (Fase 55), com grafos montados à mão.
public class BusinessFlowBuilderTests
{
    private static readonly Guid SurveyId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string Repo = "loja";
    private const string Pag = "pagamentos";

    private static readonly BusinessSource Source = new(SurveyId, Repo, "aaa111", "main",
        [new BusinessSegmentSource(Pag, "ccc333", "release")]);

    private static FlowNode M(string id, string label, string? symbol = null, NodeKind kind = NodeKind.Method,
        string? file = null, int line = 1, string? system = null, IReadOnlyList<DbAccess>? db = null) =>
        new(id, kind, label, Symbol: symbol ?? label, Location: new SourceLocation(file ?? label.Split('.')[0] + ".cs", line),
            System: system, Db: db);

    private static FlowNode Dec(string id, string condition, string? file = null, string? system = null) =>
        new(id, NodeKind.Decision, $"if ({condition})", Location: new SourceLocation(file ?? "Svc.cs", 10), Condition: condition, System: system);

    private static FlowNode Ret(string id, string label, string? file = null) =>
        new(id, NodeKind.Return, label, Location: new SourceLocation(file ?? "Svc.cs", 11));

    private static FlowNode Thr(string id, string label = "throw new Erro") =>
        new(id, NodeKind.Throw, label, Location: new SourceLocation("Svc.cs", 12));

    private static FlowEdge E(string from, string to, int order, string? label = null,
        EdgeConfidence confidence = EdgeConfidence.Extracted, HttpCall? http = null, SourceLocation? location = null) =>
        new(from, to, order, confidence, label, Http: http, Location: location);

    private static FlowGraph G(string entry, FlowNode[] nodes, FlowEdge[] edges) =>
        new(RastroSchema.Version, entry, entry, nodes, edges);

    private static BusinessFlow Derive(FlowGraph graph, BusinessViewOptions? options = null, UiLanguage language = UiLanguage.English) =>
        BusinessFlowBuilder.Derive(graph, Source, options ?? BusinessViewOptions.Default, language);

    private static string Id(string nodeId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{SurveyId:N}|{nodeId}"))).ToLowerInvariant()[..16];

    private static BusinessStep Step(BusinessFlow flow, string nodeId) => flow.Steps.Single(s => s.NodeId == nodeId);

    private static BusinessEdge Link(BusinessFlow flow, string fromNode, string toNode) =>
        flow.Edges.Single(e => e.From == Id(fromNode) && e.To == Id(toNode));

    private static readonly DbAccess[] Insert = [new("pedidos", DbOperation.Insert, 20)];
    private static readonly DbAccess[] Read = [new("pedidos", DbOperation.Read, 20)];

    // Entrada, serviço, decisão (recusa ou segue), gravação, aviso e um passo final do próprio código.
    private static FlowGraph Cancelamento() => G("m:e",
        [
            M("m:e", "Ctrl.Cancelar", kind: NodeKind.Entry),
            M("m:svc", "Svc.Cancelar"),
            Dec("if:1", "pedido.Enviado"),
            Ret("ret:1", "return BadRequest(\"enviado\")"),
            M("m:db", "Repo.Gravar", db: Insert),
            M("m:mail", "Mail.Enviar", "Fix.Pagamentos.EmailService.SendAsync(string, string)"),
            M("m:fim", "Svc.Concluir")
        ],
        [
            E("m:e", "m:svc", 1),
            E("m:svc", "if:1", 1),
            E("if:1", "ret:1", 1, "then"),
            E("m:svc", "m:db", 2),
            E("m:svc", "m:mail", 3),
            E("m:svc", "m:fim", 4)
        ]);

    [Fact]
    public void Derive_SoEntrada_GeraApenasInicio()
    {
        // CORE-BIZ-001
        var flow = Derive(G("m:e", [M("m:e", "Ctrl.Listar", kind: NodeKind.Entry)], []));

        var start = Assert.Single(flow.Steps);
        Assert.Equal(BusinessKind.Start, start.Kind);
        Assert.Equal("Ctrl.Listar", start.Label);
        Assert.Empty(flow.Edges);
        Assert.Equal(LabelOrigin.Rule, start.LabelOrigin);
        Assert.Equal(LabelState.Proposed, start.LabelState);
        Assert.Equal(RastroSchema.Version, flow.SchemaVersion);
        Assert.Equal(SurveyId, flow.SurveyId);
    }

    [Fact]
    public void Derive_FluxoCompleto_TiposOrdemELigacoes()
    {
        // CORE-BIZ-002 (R1, R2, R3, R4, R6, R7)
        var flow = Derive(Cancelamento());

        Assert.Equal(
            [BusinessKind.Start, BusinessKind.Decision, BusinessKind.Rejection, BusinessKind.Step, BusinessKind.Notification, BusinessKind.End],
            flow.Steps.Select(s => s.Kind));
        Assert.Equal(["m:e", "if:1", "ret:1", "m:db", "m:mail", "m:fim"], flow.Steps.Select(s => s.NodeId));

        Assert.Null(Link(flow, "m:e", "if:1").Label);
        Assert.Equal("then", Link(flow, "if:1", "ret:1").Label);
        Assert.Equal("else", Link(flow, "if:1", "m:db").Label);
        Assert.Equal(6, flow.Steps.Count);
        Assert.Equal(5, flow.Edges.Count);
        Assert.Equal(0, flow.CollapsedDecisions);
    }

    [Fact]
    public void Derive_Textos_SeguemRegraPorTipo()
    {
        // CORE-BIZ-003
        var flow = Derive(Cancelamento());

        Assert.Equal("pedido.Enviado?", Step(flow, "if:1").Label);
        Assert.Equal("Rejected: enviado", Step(flow, "ret:1").Label);
        Assert.Equal("Repo.Gravar", Step(flow, "m:db").Label);
        Assert.Equal("Sends email", Step(flow, "m:mail").Label);
        Assert.Equal("Svc.Concluir", Step(flow, "m:fim").Label);
    }

    [Fact]
    public void Derive_PadraoDeNotificacao_CasaTipoMetodoSemMaiusculasENamespace()
    {
        // CORE-BIZ-004 (R3)
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                M("m:n1", "N.Um", "Fix.Notificacoes.NotificationCenter.SendAsync(Guid)"),
                M("m:n2", "N.Dois", "Fix.Hubs.ChatNotifier.Notify(string)"),
                M("m:n3", "N.Tres", "Fix.Core.PedidoService.Enviar(string)"),
                M("m:n4", "N.Quatro", "Fix.Mail.smtpclient.send(string)")
            ],
            [E("m:e", "m:n1", 1), E("m:e", "m:n2", 2), E("m:e", "m:n3", 3), E("m:e", "m:n4", 4)]);

        var flow = Derive(graph);

        Assert.Equal(BusinessKind.Notification, Step(flow, "m:n1").Kind);
        Assert.Equal("In-app notice", Step(flow, "m:n1").Label);
        Assert.Equal(BusinessKind.Notification, Step(flow, "m:n2").Kind);
        Assert.Equal("N.Dois", Step(flow, "m:n2").Label);
        Assert.DoesNotContain(flow.Steps, s => s.NodeId == "m:n3" && s.Kind == BusinessKind.Notification);
        Assert.Equal(BusinessKind.Notification, Step(flow, "m:n4").Kind);
        Assert.Equal("Sends email", Step(flow, "m:n4").Label);
    }

    [Fact]
    public void Derive_NotificacaoComPadraoDaConfiguracao_UsaOsPadroesRecebidos()
    {
        // CORE-BIZ-005 (R3)
        var graph = G("m:e",
            [M("m:e", "Ctrl.A", kind: NodeKind.Entry), M("m:x", "Zap.Falar", "Fix.Zap.ZapBot.Falar(string)")],
            [E("m:e", "m:x", 1)]);

        var padrao = Derive(graph);
        var configurado = Derive(graph, BusinessViewOptions.Default with { NotificationPatterns = ["*ZapBot.Falar"] });

        Assert.DoesNotContain(padrao.Steps, s => s.Kind == BusinessKind.Notification);
        Assert.Equal(BusinessKind.Notification, Step(configurado, "m:x").Kind);
    }

    [Fact]
    public void Derive_NotificacaoPorDentro_FicaAgrupadaSemGerarOutroPasso()
    {
        // CORE-BIZ-006 (R3 e agrupamento)
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                M("m:mail", "Mail.Enviar", "Fix.EmailService.SendAsync(string)"),
                new FlowNode("x:smtp", NodeKind.External, "SmtpClient.Send", "System.Net.Mail.SmtpClient.Send(MailMessage)")
            ],
            [E("m:e", "m:mail", 1), E("m:mail", "x:smtp", 1)]);

        var flow = Derive(graph);

        Assert.Equal(2, flow.Steps.Count);
        Assert.Equal(["m:mail", "x:smtp"], Step(flow, "m:mail").SourceNodeIds);
    }

    [Theory]
    [InlineData("return BadRequest(\"x\")", true)]
    [InlineData("return NotFound()", true)]
    [InlineData("return await Task.FromResult(Conflict())", true)]
    [InlineData("return Results.Problem(\"x\")", true)]
    [InlineData("return StatusCode(404)", true)]
    [InlineData("return StatusCode(StatusCodes.Status422UnprocessableEntity, x)", true)]
    [InlineData("return StatusCode(StatusCodes.Status403Forbidden)", true)]
    [InlineData("return StatusCode(200)", false)]
    [InlineData("return Ok(x)", false)]
    [InlineData("return", false)]
    public void Derive_RetornoDeErro_VirarRecusa(string label, bool recusa)
    {
        // CORE-BIZ-007 (R4)
        var graph = G("m:e",
            [M("m:e", "Ctrl.A", kind: NodeKind.Entry), Dec("if:1", "x"), Ret("ret:1", label, "Ctrl.cs"), M("m:f", "Ctrl.Fim")],
            [E("m:e", "if:1", 1), E("if:1", "ret:1", 1, "then"), E("m:e", "m:f", 2)]);

        var flow = Derive(graph);

        Assert.Equal(recusa, flow.Steps.Any(s => s.NodeId == "ret:1" && s.Kind == BusinessKind.Rejection));
    }

    [Fact]
    public void Derive_ThrowSemCatch_VirarRecusaEThrowCapturadoNao()
    {
        // CORE-BIZ-008 (R4)
        var solto = G("m:e",
            [M("m:e", "Ctrl.A", kind: NodeKind.Entry), Dec("if:1", "x"), Thr("thr:1")],
            [E("m:e", "if:1", 1), E("if:1", "thr:1", 1, "then")]);
        var capturado = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                new FlowNode("try:1", NodeKind.Try, "try"),
                Dec("if:1", "x"),
                Thr("thr:1"),
                M("m:aviso", "Mail.Enviar", "Fix.EmailService.SendAsync(string)")
            ],
            [
                E("m:e", "try:1", 1),
                E("try:1", "if:1", 1, "try"),
                E("if:1", "thr:1", 1, "then"),
                E("try:1", "m:aviso", 1, "catch (Exception)")
            ]);

        Assert.Contains(Derive(solto).Steps, s => s.NodeId == "thr:1" && s.Kind == BusinessKind.Rejection);
        Assert.DoesNotContain(Derive(capturado).Steps, s => s.NodeId == "thr:1");
    }

    [Fact]
    public void Derive_ChamadaHttpResolvida_VirarChamadaEntreSistemasNoNoDeOrigem()
    {
        // CORE-BIZ-009 (R5)
        var local = new SourceLocation("PagamentosClient.cs", 33);
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                M("m:cli", "PagamentosClient.Estornar"),
                M(SystemIds.Prefix(Pag, "m:est"), "EstornosController.Criar", system: Pag)
            ],
            [
                E("m:e", "m:cli", 1),
                E("m:cli", SystemIds.Prefix(Pag, "m:est"), 1, http: new HttpCall("POST", "api/estornos"), location: local)
            ]);

        var flow = Derive(graph);

        var step = Step(flow, "m:cli");
        Assert.Equal(BusinessKind.CrossSystem, step.Kind);
        Assert.Equal("POST api/estornos", step.Label);
        Assert.Equal(new CodeRef(Repo, "PagamentosClient.cs", 33, "aaa111", "main"), step.Ref);
    }

    [Fact]
    public void Derive_ChamadaHttpSemDestino_VirarChamadaEntreSistemas()
    {
        // CORE-BIZ-010 (R5)
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                M("m:cli", "Avisos.Notificar"),
                new FlowNode("h:1", NodeKind.Unresolved, "POST api/avisos", Reason: UnresolvedReason.NoMatchingEndpoint)
            ],
            [E("m:e", "m:cli", 1), E("m:cli", "h:1", 1, http: new HttpCall("POST", "api/avisos"))]);

        var flow = Derive(graph);

        Assert.Equal(BusinessKind.CrossSystem, Step(flow, "m:cli").Kind);
        Assert.Equal("POST api/avisos", Step(flow, "m:cli").Label);
    }

    [Fact]
    public void Derive_EscritaNoBanco_VirarPassoELeituraNao()
    {
        // CORE-BIZ-011 (R6)
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                M("m:w", "Repo.Gravar", db: Insert),
                M("m:r", "Repo.Ler", db: Read),
                new FlowNode("x:http", NodeKind.External, "Fila.PostAsync", "Fix.Fila.PostAsync(string)")
            ],
            [E("m:e", "m:w", 1), E("m:e", "m:r", 2), E("m:e", "x:http", 3)]);

        var flow = Derive(graph);

        Assert.Equal(BusinessKind.Step, Step(flow, "m:w").Kind);
        Assert.DoesNotContain(flow.Steps, s => s.NodeId == "m:r" && s.Kind == BusinessKind.Step);
        Assert.Equal(BusinessKind.Step, Step(flow, "x:http").Kind);
    }

    [Fact]
    public void Derive_CaminhoQueTerminaSemErro_GanhaFimNoUltimoNo()
    {
        // CORE-BIZ-012 (R7)
        var flow = Derive(Cancelamento());

        var end = Step(flow, "m:fim");
        Assert.Equal(BusinessKind.End, end.Kind);
        Assert.Equal(Id("m:mail"), Assert.Single(flow.Edges, e => e.To == end.Id).From);
        Assert.DoesNotContain(flow.Steps, s => s.Kind == BusinessKind.End && s.NodeId == "ret:1");
    }

    [Fact]
    public void Derive_FimEmMetodoDeFramework_UsaOMetodoDoProprioCodigo()
    {
        // CORE-BIZ-013 (R7)
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                M("m:svc", "Svc.Fazer"),
                M("m:ok", "ControllerBase.Ok", "Microsoft.AspNetCore.Mvc.ControllerBase.Ok()"),
                M("m:w", "Repo.Gravar", db: Insert)
            ],
            [E("m:e", "m:w", 1), E("m:e", "m:svc", 2), E("m:svc", "m:ok", 1)]);

        var flow = Derive(graph);

        Assert.Equal(BusinessKind.End, Step(flow, "m:svc").Kind);
        Assert.DoesNotContain(flow.Steps, s => s.NodeId == "m:ok");
    }

    [Fact]
    public void Derive_DecisaoQueNaoMudaOResultado_SaiESeFundeAoPassoDeCima()
    {
        // CORE-BIZ-014 (R2)
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                Dec("if:1", "x"),
                M("m:log", "Log.Anotar"),
                M("m:mail", "Mail.Enviar", "Fix.EmailService.SendAsync(string)")
            ],
            [E("m:e", "if:1", 1), E("if:1", "m:log", 1, "then"), E("m:e", "m:mail", 2)]);

        var flow = Derive(graph);

        Assert.DoesNotContain(flow.Steps, s => s.Kind == BusinessKind.Decision);
        Assert.Equal([BusinessKind.Start, BusinessKind.Notification], flow.Steps.Select(s => s.Kind));
        Assert.Null(Link(flow, "m:e", "m:mail").Label);
        Assert.Equal(["m:e", "if:1", "m:log"], Step(flow, "m:e").SourceNodeIds);
    }

    [Fact]
    public void Derive_DecisaoComRamosParaPassosDiferentes_Entra()
    {
        // CORE-BIZ-015 (R2)
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                Dec("if:1", "x"),
                M("m:w", "Repo.Gravar", db: Insert),
                M("m:mail", "Mail.Enviar", "Fix.EmailService.SendAsync(string)")
            ],
            [E("m:e", "if:1", 1), E("if:1", "m:w", 1, "then"), E("if:1", "m:mail", 2, "else")]);

        var flow = Derive(graph);

        Assert.Equal([BusinessKind.Start, BusinessKind.Decision, BusinessKind.Step, BusinessKind.Notification], flow.Steps.Select(s => s.Kind));
        Assert.Equal("then", Link(flow, "if:1", "m:w").Label);
        Assert.Equal("else", Link(flow, "if:1", "m:mail").Label);
    }

    [Fact]
    public void Derive_SwitchComCases_RotuloDoRamoNaLigacao()
    {
        // CORE-BIZ-016 (rótulo do ramo)
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                new FlowNode("sw:1", NodeKind.Decision, "switch (meio)", Condition: "meio", Location: new SourceLocation("Svc.cs", 5)),
                M("m:mail", "Mail.Enviar", "Fix.EmailService.SendAsync(string)"),
                Thr("thr:1")
            ],
            [E("m:e", "sw:1", 1), E("sw:1", "m:mail", 1, "case Cartao"), E("sw:1", "thr:1", 2, "case Boleto")]);

        var flow = Derive(graph);

        Assert.Equal("case Cartao", Link(flow, "sw:1", "m:mail").Label);
        Assert.Equal("case Boleto", Link(flow, "sw:1", "thr:1").Label);
        Assert.Equal("meio?", Step(flow, "sw:1").Label);
    }

    [Fact]
    public void Derive_NosForaDosPassos_FicamAgrupadosNoPassoAnterior()
    {
        // CORE-BIZ-017 (agrupamento)
        var flow = Derive(Cancelamento());

        Assert.Equal(["m:e", "m:svc"], Step(flow, "m:e").SourceNodeIds);
        Assert.Equal(["m:db"], Step(flow, "m:db").SourceNodeIds);
        Assert.Equal(["if:1"], Step(flow, "if:1").SourceNodeIds);
    }

    [Fact]
    public void Derive_ArestaInferidaNoCaminho_LigacaoInferida()
    {
        // CORE-BIZ-018
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                M("m:a", "Svc.A"),
                M("m:mail", "Mail.Enviar", "Fix.EmailService.SendAsync(string)"),
                M("m:w", "Repo.Gravar", db: Insert)
            ],
            [
                E("m:e", "m:a", 1, confidence: EdgeConfidence.Inferred),
                E("m:a", "m:mail", 1),
                E("m:e", "m:w", 2)
            ]);

        var flow = Derive(graph);

        Assert.True(Link(flow, "m:e", "m:mail").Inferred);
        Assert.False(Link(flow, "m:mail", "m:w").Inferred);
        Assert.False(Step(flow, "m:mail").Inferred);
    }

    [Fact]
    public void Derive_ArestaInferidaNoProprioPasso_PassoInferido()
    {
        // CORE-BIZ-019
        var graph = G("m:e",
            [M("m:e", "Ctrl.A", kind: NodeKind.Entry), M("m:mail", "Mail.Enviar", "Fix.EmailService.SendAsync(string)")],
            [E("m:e", "m:mail", 1, confidence: EdgeConfidence.Inferred)]);

        var flow = Derive(graph);

        Assert.True(Step(flow, "m:mail").Inferred);
        Assert.True(Assert.Single(flow.Edges).Inferred);
    }

    [Fact]
    public void Derive_RecursaoELaco_TerminaSemTravar()
    {
        // CORE-BIZ-020
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                M("m:a", "Svc.A"),
                M("m:b", "Svc.B"),
                new FlowNode("loop:1", NodeKind.Loop, "foreach (x in xs)", Condition: "xs"),
                M("m:mail", "Mail.Enviar", "Fix.EmailService.SendAsync(string)")
            ],
            [
                E("m:e", "m:a", 1),
                E("m:a", "m:b", 1),
                E("m:b", "m:a", 1),
                E("m:a", "loop:1", 2),
                E("loop:1", "m:mail", 1, "each"),
                E("m:mail", "loop:1", 1)
            ]);

        var flow = Derive(graph);

        Assert.Equal(1, flow.Steps.Count(s => s.Kind == BusinessKind.Notification));
        Assert.Equal(BusinessKind.Start, flow.Steps[0].Kind);
    }

    [Fact]
    public void Derive_PassouDoLimite_FundeAsDecisoesMaisProfundasSemAvisoNemRecusa()
    {
        // CORE-BIZ-021 (limite)
        var nodes = new List<FlowNode> { M("m:e", "Ctrl.A", kind: NodeKind.Entry) };
        var edges = new List<FlowEdge>();
        var parent = "m:e";
        for (var i = 1; i <= 5; i++)
        {
            nodes.Add(M($"m:c{i}", $"Svc.C{i}"));
            nodes.Add(Dec($"if:{i}", $"cond{i}"));
            nodes.Add(M($"m:w{i}", $"Repo.W{i}", db: Insert));
            edges.Add(E(parent, $"m:c{i}", 1));
            edges.Add(E($"m:c{i}", $"if:{i}", 1));
            edges.Add(E($"if:{i}", $"m:w{i}", 1, "then"));
            parent = $"m:c{i}";
        }
        // A decisão mais funda leva a recusa e por isso nunca é fundida.
        nodes.Add(Dec("if:z", "condz"));
        nodes.Add(Ret("ret:z", "return NotFound()"));
        edges.Add(E("m:c5", "if:z", 2));
        edges.Add(E("if:z", "ret:z", 1, "then"));

        var graph = G("m:e", [.. nodes], [.. edges]);
        var inteiro = Derive(graph);
        var limitado = Derive(graph, BusinessViewOptions.Default with { MaxStepsPerFlow = 9 });

        Assert.Equal(0, inteiro.CollapsedDecisions);
        Assert.True(limitado.Steps.Count <= 9);
        Assert.True(limitado.CollapsedDecisions > 0);
        var decisoes = limitado.Steps.Where(s => s.Kind == BusinessKind.Decision).Select(s => s.NodeId).ToList();
        Assert.Contains("if:z", decisoes);
        Assert.Contains("if:1", decisoes);
        Assert.DoesNotContain("if:5", decisoes);
        Assert.Equal(inteiro.Steps.Count - limitado.Steps.Count, limitado.CollapsedDecisions);
    }

    [Fact]
    public void Derive_MesmoGrafo_MesmoIdEMudaComOSurveyId()
    {
        // CORE-BIZ-022
        var a = Derive(Cancelamento());
        var b = Derive(Cancelamento());
        var outro = BusinessFlowBuilder.Derive(Cancelamento(),
            Source with { SurveyId = Guid.Parse("99999999-2222-3333-4444-555555555555") },
            BusinessViewOptions.Default, UiLanguage.English);

        Assert.Equal(a.Steps.Select(s => s.Id), b.Steps.Select(s => s.Id));
        Assert.Equal(Id("m:e"), a.Steps[0].Id);
        Assert.Equal(16, a.Steps[0].Id.Length);
        Assert.Matches("^[0-9a-f]{16}$", a.Steps[0].Id);
        Assert.NotEqual(a.Steps[0].Id, outro.Steps[0].Id);
        Assert.All(a.Edges, e => Assert.Contains(a.Steps, s => s.Id == e.From));
    }

    [Fact]
    public void Derive_ReferenciaDoCodigo_UsaRepositorioECommitDoTrecho()
    {
        // CORE-BIZ-023
        var remote = SystemIds.Prefix(Pag, "m:mail");
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry, file: "src\\Ctrl.cs", line: 7),
                M("m:cli", "Cli.Estornar"),
                M(SystemIds.Prefix(Pag, "m:est"), "Estornos.Criar", system: Pag),
                M(remote, "Mail.Enviar", "Fix.EmailService.SendAsync(string)", file: "Mail.cs", line: 42, system: Pag)
            ],
            [
                E("m:e", "m:cli", 1),
                E("m:cli", SystemIds.Prefix(Pag, "m:est"), 1, http: new HttpCall("POST", "api/estornos")),
                E(SystemIds.Prefix(Pag, "m:est"), remote, 1)
            ]);

        var flow = Derive(graph);

        Assert.Equal(new CodeRef(Repo, "src/Ctrl.cs", 7, "aaa111", "main"), Step(flow, "m:e").Ref);
        Assert.Equal(new CodeRef(Pag, "Mail.cs", 42, "ccc333", "release"), Step(flow, remote).Ref);
        Assert.Equal(BusinessKind.CrossSystem, Step(flow, "m:cli").Kind);
    }

    [Fact]
    public void Derive_NoSemLocalizacao_PassoSemReferencia()
    {
        // CORE-BIZ-024
        var graph = G("m:e",
            [M("m:e", "Ctrl.A", kind: NodeKind.Entry), new FlowNode("x:smtp", NodeKind.External, "SmtpClient.Send", "System.Net.Mail.SmtpClient.Send(MailMessage)")],
            [E("m:e", "x:smtp", 1)]);

        var flow = Derive(graph);

        Assert.Null(Step(flow, "x:smtp").Ref);
        Assert.Equal(BusinessKind.Notification, Step(flow, "x:smtp").Kind);
    }

    [Theory]
    [InlineData(UiLanguage.English, "Sends email", "cond?", "Rejected: Erro")]
    [InlineData(UiLanguage.Portuguese, "Envia e-mail", "cond?", "Recusa: Erro")]
    [InlineData(UiLanguage.Spanish, "Envía correo", "¿cond?", "Rechazo: Erro")]
    public void Derive_Idioma_TextosNosTresIdiomas(UiLanguage language, string email, string decisao, string recusa)
    {
        // CORE-BIZ-025
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                Dec("if:1", "cond"),
                Thr("thr:1"),
                M("m:mail", "Mail.Enviar", "Fix.EmailService.SendAsync(string)")
            ],
            [E("m:e", "if:1", 1), E("if:1", "thr:1", 1, "then"), E("m:e", "m:mail", 2)]);

        var flow = Derive(graph, language: language);

        Assert.Equal(email, Step(flow, "m:mail").Label);
        Assert.Equal(decisao, Step(flow, "if:1").Label);
        Assert.Equal(recusa, Step(flow, "thr:1").Label);
    }

    [Theory]
    [InlineData(UiLanguage.English, "In-app notice")]
    [InlineData(UiLanguage.Portuguese, "Aviso no app")]
    [InlineData(UiLanguage.Spanish, "Aviso en la app")]
    public void Derive_AvisoNoApp_TextoPorIdioma(UiLanguage language, string esperado)
    {
        // CORE-BIZ-026
        var graph = G("m:e",
            [M("m:e", "Ctrl.A", kind: NodeKind.Entry), M("m:n", "N.Avisar", "Fix.NotificationService.Send(string)")],
            [E("m:e", "m:n", 1)]);

        Assert.Equal(esperado, Step(Derive(graph, language: language), "m:n").Label);
    }

    [Fact]
    public void Derive_GuardaDoAngular_VirarBloqueadoNaTela()
    {
        // CORE-BIZ-027 (R4 no Angular)
        var graph = G("m:e",
            [
                M("m:e", "DetalhePedidoComponent.cancelar", kind: NodeKind.Entry, file: "detalhe-pedido.component.ts"),
                new FlowNode("if:html", NodeKind.Decision, "if (!(pedido.enviado))", Location: new SourceLocation("detalhe-pedido.component.html", 4),
                    Condition: "!(pedido.enviado)"),
                new FlowNode("if:ts", NodeKind.Decision, "if (this.pedido.enviado)", Location: new SourceLocation("detalhe-pedido.component.ts", 20),
                    Condition: "this.pedido.enviado"),
                new FlowNode("ret:ts", NodeKind.Return, "return", Location: new SourceLocation("detalhe-pedido.component.ts", 21)),
                M("m:svc", "PedidosService.cancelar", file: "pedidos.service.ts", line: 9),
                M(SystemIds.Prefix("api-pedidos", "m:ctrl"), "PedidosController.Cancelar", system: "api-pedidos")
            ],
            [
                E("m:e", "if:html", 1),
                E("if:html", "if:ts", 1, "then"),
                E("if:ts", "ret:ts", 1, "then"),
                E("if:html", "m:svc", 2, "then"),
                E("m:svc", SystemIds.Prefix("api-pedidos", "m:ctrl"), 1, http: new HttpCall("POST", "api/pedidos/{}/cancelamento"))
            ]);

        var flow = Derive(graph, language: UiLanguage.Portuguese);

        Assert.Equal(BusinessKind.Rejection, Step(flow, "if:html").Kind);
        Assert.Equal("Bloqueado na tela", Step(flow, "if:html").Label);
        Assert.Equal("detalhe-pedido.component.html", Step(flow, "if:html").Ref!.File);
        Assert.Equal(BusinessKind.Decision, Step(flow, "if:ts").Kind);
        Assert.Equal("this.pedido.enviado?", Step(flow, "if:ts").Label);
        Assert.Equal(BusinessKind.Rejection, Step(flow, "ret:ts").Kind);
        Assert.Equal("Bloqueado na tela", Step(flow, "ret:ts").Label);
        Assert.Equal("POST api/pedidos/{}/cancelamento", Step(flow, "m:svc").Label);
        Link(flow, "m:e", "if:html");
        Assert.Equal("then", Link(flow, "if:ts", "ret:ts").Label);
    }

    [Fact]
    public void Derive_DuasDerivacoes_ProduzemOMesmoResultado()
    {
        // CORE-BIZ-028 (determinismo)
        var a = JsonSerializer.Serialize(Derive(Cancelamento(), language: UiLanguage.Portuguese));
        var b = JsonSerializer.Serialize(Derive(Cancelamento(), language: UiLanguage.Portuguese));

        Assert.Equal(a, b);
    }

    [Fact]
    public void Derive_SemNoDeEntrada_VisaoVazia()
    {
        // CORE-BIZ-029
        var flow = Derive(G("m:nada", [M("m:e", "Ctrl.A", kind: NodeKind.Entry)], []));

        Assert.Empty(flow.Steps);
        Assert.Empty(flow.Edges);
    }

    [Fact]
    public void Derive_IfSemElseQueSegueAteOFim_LigacaoElseDaDecisao()
    {
        // CORE-BIZ-031 (ramo implícito)
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                Dec("if:1", "cartao"),
                M("m:mail", "Mail.Enviar", "Fix.EmailService.SendAsync(string)"),
                M("m:fim", "Svc.Concluir")
            ],
            [E("m:e", "if:1", 1), E("if:1", "m:mail", 1, "then"), E("m:e", "m:fim", 2)]);

        var flow = Derive(graph);

        Assert.Equal(BusinessKind.Decision, Step(flow, "if:1").Kind);
        Assert.Equal("then", Link(flow, "if:1", "m:mail").Label);
        Assert.Equal("else", Link(flow, "if:1", "m:fim").Label);
        Assert.Null(Link(flow, "m:mail", "m:fim").Label);
        Assert.Equal(BusinessKind.End, Step(flow, "m:fim").Kind);
    }

    [Fact]
    public void Derive_FimDoCaminho_PrimeiroMetodoDoCodigoNoNivelMaisDeFora()
    {
        // CORE-BIZ-032 (R7: callbacks achatados do subscribe)
        var graph = G("m:e",
            [
                M("m:e", "Comp.cancelar", kind: NodeKind.Entry),
                M("m:a", "Svc.Fazer"),
                M("m:w", "Repo.Gravar", db: Insert),
                M("m:x", "Svc.Depois"),
                M("m:rec", "Comp.recarregar"),
                M("m:err", "Comp.mostrarErro"),
                M("m:ok", "ControllerBase.Ok", "Microsoft.AspNetCore.Mvc.ControllerBase.Ok()")
            ],
            [
                E("m:e", "m:a", 1),
                E("m:a", "m:w", 1),
                E("m:a", "m:x", 2),
                E("m:x", "m:ok", 1),
                E("m:e", "m:rec", 2),
                E("m:e", "m:err", 3)
            ]);

        var flow = Derive(graph);

        Assert.Equal(BusinessKind.End, Step(flow, "m:rec").Kind);
        Assert.DoesNotContain(flow.Steps, s => s.NodeId is "m:err" or "m:x" or "m:ok");
        Link(flow, "m:w", "m:rec");
    }

    [Theory]
    [InlineData(UiLanguage.English, "Rejected: Fora do prazo.", "Rejected: NotFound", "Rejected: PedidoJaEnviado")]
    [InlineData(UiLanguage.Portuguese, "Recusa: Fora do prazo.", "Recusa: NotFound", "Recusa: PedidoJaEnviado")]
    [InlineData(UiLanguage.Spanish, "Rechazo: Fora do prazo.", "Rechazo: NotFound", "Rechazo: PedidoJaEnviado")]
    public void Derive_TextoDaRecusa_UsaLiteralOuNomeDoTipoSemReturn(UiLanguage language, string comLiteral, string semLiteral, string excecao)
    {
        // CORE-BIZ-033
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                Dec("if:1", "a"), Ret("ret:1", "return BadRequest(\"Fora do prazo.\")"),
                Dec("if:2", "b"), Ret("ret:2", "return NotFound()"),
                Dec("if:3", "c"), Thr("thr:3", "throw new PedidoJaEnviado(id)"),
                M("m:fim", "Ctrl.Fim")
            ],
            [
                E("m:e", "if:1", 1), E("if:1", "ret:1", 1, "then"),
                E("m:e", "if:2", 2), E("if:2", "ret:2", 1, "then"),
                E("m:e", "if:3", 3), E("if:3", "thr:3", 1, "then"),
                E("m:e", "m:fim", 4)
            ]);

        var flow = Derive(graph, language: language);

        Assert.Equal(comLiteral, Step(flow, "ret:1").Label);
        Assert.Equal(semLiteral, Step(flow, "ret:2").Label);
        Assert.Equal(excecao, Step(flow, "thr:3").Label);
    }

    [Fact]
    public void Derive_ReturnNaoErroDentroDoServico_NaoEncerraOFluxo()
    {
        // CORE-BIZ-030 (saída do método continua no chamador)
        var graph = G("m:e",
            [
                M("m:e", "Ctrl.A", kind: NodeKind.Entry),
                M("m:svc", "Svc.Preparar"),
                Dec("if:1", "x"),
                Ret("ret:1", "return"),
                M("m:mail", "Mail.Enviar", "Fix.EmailService.SendAsync(string)")
            ],
            [E("m:e", "m:svc", 1), E("m:svc", "if:1", 1), E("if:1", "ret:1", 1, "then"), E("m:e", "m:mail", 2)]);

        var flow = Derive(graph);

        Assert.Equal(BusinessKind.Notification, Step(flow, "m:mail").Kind);
        Assert.DoesNotContain(flow.Steps, s => s.Kind == BusinessKind.Rejection);
    }
}
