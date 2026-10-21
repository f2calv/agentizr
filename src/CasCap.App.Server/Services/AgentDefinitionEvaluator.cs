using CasCap.AgentRuntime.Contracts.V1;
using CasCap.Common.Extensions;
using CasCap.Common.Services;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CasCap.Services;

/// <summary>
/// Runs fixture-backed scenarios against one immutable definition before it is activated. The definition's
/// real tool surface is composed, but no external tool is ever executed: query and delegation tools return
/// scenario fixtures, actuating tools are recorded and sandboxed, and only the runtime's read-only built-ins run.
/// </summary>
internal sealed class AgentDefinitionEvaluator(
    IEvaluationAgentFactory agentFactory,
    ILoggerFactory loggerFactory,
    IServiceProvider serviceProvider,
    IAgentDefinitionStore definitionStore,
    AgentRuntimeBuiltInTools builtInTools,
    IProviderCredentialStore credentialStore,
    IMcpCredentialStore mcpCredentialStore,
    IOptions<AgentDefinitionPolicyConfig> policyConfig)
{
    private static readonly TimeSpan RegexValidationTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Returns a public-safe reason the request cannot be evaluated, or <see langword="null" />.</summary>
    public static string? Validate(EvaluateAgentDefinitionRequest request)
    {
        if (request.Scenarios is not { Count: > 0 })
            return "An evaluation requires at least one scenario.";
        if (request.Scenarios.Count > 50)
            return "An evaluation cannot contain more than 50 scenarios.";
        if (request.Repetitions is < 1 or > 10)
            return "Repetitions must be between 1 and 10.";
        if (request.MinimumPassRate is < 0d or > 1d || double.IsNaN(request.MinimumPassRate))
            return "Minimum pass rate must be between 0 and 1.";

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in request.Scenarios)
        {
            if (scenario is null || string.IsNullOrWhiteSpace(scenario.Id) || scenario.Id.Length > 100)
                return "Every scenario requires an identifier of 1 to 100 characters.";
            if (!ids.Add(scenario.Id))
                return $"Scenario identifier '{scenario.Id}' is duplicated.";
            if (string.IsNullOrWhiteSpace(scenario.Question) || scenario.Question.Length > 10_000)
                return $"Scenario '{scenario.Id}' requires a question of 1 to 10000 characters.";
            if (scenario.Answer is null || scenario.RequiredToolGroups is null || scenario.ForbiddenTools is null
                || scenario.ToolResponses is null || scenario.Answer.ContainsAny is null || scenario.Answer.ContainsNone is null)
                return $"Scenario '{scenario.Id}' cannot contain null collections.";
            if (scenario.RequiredToolGroups.Any(group => group is not { Length: > 0 }))
                return $"Scenario '{scenario.Id}' contains an empty required tool group.";
            if (scenario.Answer.Pattern is { } pattern)
            {
                try
                {
                    _ = new Regex(pattern, RegexOptions.None, RegexValidationTimeout);
                }
                catch (ArgumentException)
                {
                    return $"Scenario '{scenario.Id}' answer pattern is not a valid regular expression.";
                }
            }
        }
        return null;
    }

    /// <summary>Runs every scenario repetition and grades the outcome.</summary>
    public async ValueTask<AgentEvaluationResponse> EvaluateAsync(
        AgentDefinition definition,
        EvaluateAgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger<AgentDefinitionEvaluator>();
        var apiKey = await credentialStore.GetApiKeyAsync(definition.Agent.Provider, cancellationToken);
        var provider = definition.Provider with { ApiKey = apiKey };
        var timeout = TimeSpan.FromSeconds(policyConfig.Value.EvaluationRunTimeoutSeconds);
        var toolLeases = new List<IAsyncDisposable>();
        try
        {
            var toolSurface = await ComposeToolSurfaceAsync(definition, toolLeases, logger, cancellationToken);
            var runs = new List<AgentEvaluationRunResponse>();
            foreach (var scenario in request.Scenarios)
            {
                for (var repetition = 1; repetition <= request.Repetitions; repetition++)
                    runs.Add(await RunAsync(definition, provider, toolSurface, scenario, repetition, timeout, logger, cancellationToken));
            }

            var passedRuns = runs.Count(run => run.Passed);
            var passRate = (double)passedRuns / runs.Count;
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation(
                    "{ClassName} evaluated {AgentName} version {DefinitionVersion}: {PassedRuns} of {TotalRuns} runs passed",
                    nameof(AgentDefinitionEvaluator),
                    definition.Name,
                    definition.Version,
                    passedRuns,
                    runs.Count);
            return new AgentEvaluationResponse
            {
                AgentName = definition.Name,
                DefinitionVersion = definition.Version,
                Passed = passRate >= request.MinimumPassRate,
                PassRate = passRate,
                MinimumPassRate = request.MinimumPassRate,
                Runs = runs,
            };
        }
        finally
        {
            foreach (var lease in toolLeases)
                await lease.DisposeAsync();
        }
    }

    private async ValueTask<AgentEvaluationRunResponse> RunAsync(
        AgentDefinition definition,
        ProviderConfig provider,
        IReadOnlyList<(AIFunction Tool, ToolDisposition Disposition)> toolSurface,
        AgentEvaluationScenarioRequest scenario,
        int repetition,
        TimeSpan timeout,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var recorder = new EvaluationRecorder();
        var tools = toolSurface
            .Select(entry => (AITool)new FixtureToolFunction(
                entry.Tool,
                definition.Name,
                entry.Disposition,
                scenario.ToolResponses.TryGetValue(entry.Tool.Name, out var fixture)
                    ? _ => fixture
                    : null,
                recorder))
            .ToList();

        var answer = string.Empty;
        string? error = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var (agent, instructions) = agentFactory.Create(provider, definition.Agent, tools);
            var result = await agent.RunAnalysisAsync(
                provider,
                definition.Agent,
                AgentExtensions.BuildChatMessage(scenario.Question),
                AgentExtensions.BuildChatOptions(definition.Agent, instructions),
                timeout: timeout,
                cancellationToken: cancellationToken,
                logger: logger);
            answer = result.OutputText;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
        }
        stopwatch.Stop();

        var toolCalls = recorder.ToolCalls;
        var (answerPassed, toolSelectionPassed, reasons) = AgentEvaluationGrader.Grade(
            MapScenario(definition.Name, scenario),
            answer,
            toolCalls);
        return new AgentEvaluationRunResponse
        {
            ScenarioId = scenario.Id,
            Repetition = repetition,
            Passed = error is null && answerPassed && toolSelectionPassed,
            AnswerPassed = answerPassed,
            ToolSelectionPassed = toolSelectionPassed,
            Answer = answer,
            FailureReasons = error is null ? reasons : [$"run failed: {error}", .. reasons],
            ToolCalls = toolCalls.Select(call => call.ToolName).ToArray(),
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            Error = error,
        };
    }

    /// <summary>
    /// Composes the definition's model-facing tools with the disposition that decides how each is sandboxed.
    /// Built-ins are tenant-scoped and read-only, so they run for real via the pass-through disposition.
    /// </summary>
    private async ValueTask<IReadOnlyList<(AIFunction Tool, ToolDisposition Disposition)>> ComposeToolSurfaceAsync(
        AgentDefinition definition,
        List<IAsyncDisposable> toolLeases,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var tools = new List<AITool>();
        var dispositions = new Dictionary<string, ToolDisposition>(StringComparer.OrdinalIgnoreCase);

        void Add(IEnumerable<AITool> candidates, string source, Func<AITool, ToolDisposition> classify)
        {
            var before = tools.Count;
            CommonAgentExecutor.AddUniqueTools(tools, candidates, source, logger);
            foreach (var tool in tools.Skip(before))
                dispositions[tool.Name] = classify(tool);
        }

        Add(await builtInTools.CreateAsync(definition, cancellationToken), "runtime built-ins", _ => ToolDisposition.Delegation);
        foreach (var source in definition.Agent.Tools)
        {
            if (source.Service is not null)
            {
                Add(
                    AgentExtensions.CreateToolsForAgent(
                        serviceProvider,
                        definition.Agent with { Tools = [source] },
                        deferResolution: true,
                        logger: logger),
                    "service tools",
                    _ => ToolDisposition.Query);
                continue;
            }

            if (source.Endpoint is not null)
            {
                Add(
                    await CommonAgentExecutor.CreateRemoteToolsAsync(
                        source,
                        tools,
                        mcpCredentialStore,
                        toolLeases,
                        logger,
                        cancellationToken),
                    "remote MCP tools",
                    tool => CommonAgentExecutor.IsReadOnlyRemoteTool(tool) ? ToolDisposition.Query : ToolDisposition.SideEffect);
                continue;
            }

            if (source.Agent is not null)
                Add([await CreateDelegationDeclarationAsync(source, logger, cancellationToken)], "delegation", _ => ToolDisposition.Query);
        }

        return tools
            .OfType<AIFunction>()
            .Select(tool => (tool, dispositions[tool.Name]))
            .ToArray();
    }

    /// <summary>
    /// Declares a delegation tool with the production name, description and schema. It is never invoked:
    /// the scenario fixture stands in for the sub-agent, so one definition is evaluated in isolation.
    /// </summary>
    private async ValueTask<AITool> CreateDelegationDeclarationAsync(
        ToolSource source,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var agentName = source.Agent!;
        var delegated = await definitionStore.GetAsync(agentName, cancellationToken)
            ?? throw new InvalidOperationException($"Delegated agent '{agentName}' is not available to the current tenant.");
        var declaration = AIFunctionFactory.Create(
            ([Description("The task or question to pass to this specialist agent.")] string task) => string.Empty,
            new AIFunctionFactoryOptions
            {
                Name = $"invoke_{agentName.ToSnakeCase()}",
                Description = delegated.Agent.Description,
            });
        return AgentExtensions.FilterTools([declaration], source, logger).Single();
    }

    private static AgentEvaluationScenario MapScenario(string agentName, AgentEvaluationScenarioRequest scenario) => new()
    {
        Id = scenario.Id,
        AgentKey = agentName,
        Question = scenario.Question,
        Answer = MapAnswer(scenario.Answer),
        RequiredToolGroups = scenario.RequiredToolGroups,
        ForbiddenTools = scenario.ForbiddenTools,
        SideEffectsForbidden = scenario.SideEffectsForbidden,
    };

    private static AnswerExpectation MapAnswer(AgentEvaluationAnswerRequest answer)
    {
        var expectations = new List<AnswerExpectation>();
        if (answer.ContainsAny.Count > 0)
            expectations.Add(AnswerExpectation.ContainsAny([.. answer.ContainsAny]));
        if (answer.ContainsNone.Count > 0)
            expectations.Add(AnswerExpectation.ContainsNone([.. answer.ContainsNone]));
        if (answer.Pattern is { } pattern)
            expectations.Add(AnswerExpectation.Matches(pattern, $"matches /{pattern}/"));
        if (answer.Number is { } number)
            expectations.Add(AnswerExpectation.Number(number, answer.NumberTolerance));
        return expectations.Count switch
        {
            0 => new AnswerExpectation("gives an answer", _ => true),
            1 => expectations[0],
            _ => AnswerExpectation.All([.. expectations]),
        };
    }
}
