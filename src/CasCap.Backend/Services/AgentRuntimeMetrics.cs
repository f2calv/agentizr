using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CasCap.Services;

/// <summary>Exports bounded tenant-scoped metrics for Agent Runtime executions.</summary>
public sealed class AgentRuntimeMetrics
{
    private readonly Counter<long> _runs;
    private readonly Histogram<double> _runDuration;
    private readonly Histogram<double> _timeToFirstToken;
    private readonly Counter<long> _tokens;
    private readonly Counter<long> _toolCalls;
    private readonly Counter<long> _reasoningMarkup;
    private readonly Counter<long> _haOperations;
    private readonly Histogram<double> _haDuration;
    private readonly UpDownCounter<long> _sessionOwners;

    /// <summary>Creates Agent Runtime instruments on the configured application meter.</summary>
    public AgentRuntimeMetrics(IOptions<AppConfig> appConfig, IMeterFactory meterFactory)
    {
        var prefix = appConfig.Value.MetricNamePrefix;
        var meter = meterFactory.Create(prefix);
        _haOperations = meter.CreateCounter<long>(
            $"{prefix}.ha.operations", unit: "{operation}",
            description: "Persistence, cache, lock and conversation-history outcomes.");
        _haDuration = meter.CreateHistogram<double>(
            $"{prefix}.ha.duration", unit: "ms",
            description: "Database state write and session lock acquisition duration.");
        _sessionOwners = meter.CreateUpDownCounter<long>(
            $"{prefix}.ha.session.owners", unit: "{session}",
            description: "Session mutations currently holding a PostgreSQL advisory lock.");
        _runs = meter.CreateCounter<long>(
            $"{prefix}.run",
            unit: "{run}",
            description: "Agent runs by tenant, definition, provider, model and outcome.");
        _runDuration = meter.CreateHistogram<double>(
            $"{prefix}.run.duration",
            unit: "ms",
            description: "End-to-end Agent Runtime execution duration.");
        _timeToFirstToken = meter.CreateHistogram<double>(
            $"{prefix}.run.time_to_first_token",
            unit: "ms",
            description: "Time from provider dispatch to the first text-bearing response update.");
        _tokens = meter.CreateCounter<long>(
            $"{prefix}.tokens",
            unit: "{token}",
            description: "Provider-reported token usage by tenant, definition and token kind.");
        _toolCalls = meter.CreateCounter<long>(
            $"{prefix}.tool.calls",
            unit: "{call}",
            description: "Tool calls completed during Agent Runtime executions, including delegated agents, by tool outcome.");
        _reasoningMarkup = meter.CreateCounter<long>(
            $"{prefix}.reasoning_markup",
            unit: "{run}",
            description: "Agent runs, including delegated agents, whose answer leaked reasoning markup that was removed.");
    }

    /// <summary>Records a run whose requested definition was unavailable.</summary>
    public void RecordUnavailable(string tenant, string agent, TimeSpan duration)
    {
        var tags = Tags(tenant, agent, outcome: "unavailable");
        _runs.Add(1, tags);
        _runDuration.Record(duration.TotalMilliseconds, tags);
    }

    /// <summary>Records a completed run and its provider diagnostics.</summary>
    /// <param name="tenant">The tenant that ran the agent.</param>
    /// <param name="definition">The executed definition.</param>
    /// <param name="model">The effective model.</param>
    /// <param name="duration">The end-to-end duration.</param>
    /// <param name="diagnostics">The top-level run diagnostics.</param>
    /// <param name="delegatedRuns">Diagnostics of sub-agent runs, whose tool calls are counted too.</param>
    /// <remarks>A run whose tools failed is recorded as <c>completed_with_tool_errors</c> rather than <c>succeeded</c>.</remarks>
    public void RecordCompleted(
        string tenant,
        AgentDefinition definition,
        string model,
        TimeSpan duration,
        AgentRunResult? diagnostics,
        IEnumerable<AgentRunResult> delegatedRuns)
    {
        AgentRunResult[] runs = diagnostics is null ? [.. delegatedRuns] : [diagnostics, .. delegatedRuns];
        var toolCalls = runs.Sum(run => run.ToolCallCount);
        var toolErrors = runs.Sum(run => run.ToolErrorCount);
        var tags = Tags(tenant, definition.Name, toolErrors > 0 ? "completed_with_tool_errors" : "succeeded",
            definition.Agent.Provider, model);
        _runs.Add(1, tags);
        _runDuration.Record(duration.TotalMilliseconds, tags);
        if (diagnostics?.TimeToFirstToken is { } firstToken)
            _timeToFirstToken.Record(firstToken.TotalMilliseconds, tags);
        RecordToolCalls(toolCalls - toolErrors, "succeeded", tags);
        RecordToolCalls(toolErrors, "error", tags);
        var reasoningMarkupRuns = runs.Count(run => run.ReasoningMarkupCharactersRemoved > 0);
        if (reasoningMarkupRuns > 0)
            _reasoningMarkup.Add(reasoningMarkupRuns, tags);
        if (diagnostics?.Usage is { } usage)
        {
            RecordTokens(usage.InputTokenCount, "input", tags);
            RecordTokens(usage.OutputTokenCount, "output", tags);
            RecordTokens(usage.ReasoningTokenCount, "reasoning", tags);
        }
    }

    /// <summary>Records a failed, cancelled or conflicting run.</summary>
    public void RecordFailed(
        string tenant,
        string agent,
        AgentDefinition? definition,
        TimeSpan duration,
        bool cancelled,
        bool conflict = false)
    {
        var tags = Tags(
            tenant,
            agent,
            conflict ? "conflict" : cancelled ? "cancelled" : "failed",
            definition?.Agent.Provider,
            definition?.Provider.ModelName);
        _runs.Add(1, tags);
        _runDuration.Record(duration.TotalMilliseconds, tags);
    }

    /// <summary>Records a bounded HA operation outcome without session, run or cache-key identifiers.</summary>
    public void RecordHa(string tenant, string operation, string outcome, TimeSpan? duration = null, long count = 1)
    {
        var tags = new TagList([new("tenant", tenant), new("operation", operation), new("outcome", outcome)]);
        _haOperations.Add(count, tags);
        if (duration is { } elapsed)
            _haDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    /// <summary>Records acquisition or release of a connection-bound session owner.</summary>
    public void RecordSessionOwner(string tenant, bool acquired) =>
        _sessionOwners.Add(acquired ? 1 : -1, new TagList([new("tenant", tenant)]));

    private void RecordToolCalls(int count, string toolOutcome, TagList tags)
    {
        if (count <= 0)
            return;
        tags.Add("tool_outcome", toolOutcome);
        _toolCalls.Add(count, tags);
    }

    private void RecordTokens(long? count, string kind, TagList tags)
    {
        if (count is not > 0)
            return;
        tags.Add("token_kind", kind);
        _tokens.Add(count.Value, tags);
    }

    private static TagList Tags(
        string tenant,
        string agent,
        string outcome,
        string? provider = null,
        string? model = null)
    {
        var tags = new TagList
        {
            { "tenant", tenant },
            { "agent", agent },
            { "outcome", outcome },
        };
        if (!string.IsNullOrWhiteSpace(provider))
            tags.Add("provider", provider);
        if (!string.IsNullOrWhiteSpace(model))
            tags.Add("model", model);
        return tags;
    }
}
