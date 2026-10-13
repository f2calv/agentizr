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

    /// <summary>Creates Agent Runtime instruments on the configured application meter.</summary>
    public AgentRuntimeMetrics(IOptions<AppConfig> appConfig, IMeterFactory meterFactory)
    {
        var prefix = appConfig.Value.MetricNamePrefix;
        var meter = meterFactory.Create(prefix);
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
        if (diagnostics?.Usage is { } usage)
        {
            RecordTokens(usage.InputTokenCount, "input", tags);
            RecordTokens(usage.OutputTokenCount, "output", tags);
            RecordTokens(usage.ReasoningTokenCount, "reasoning", tags);
        }
    }

    /// <summary>Records a failed or cancelled run.</summary>
    public void RecordFailed(
        string tenant,
        string agent,
        AgentDefinition? definition,
        TimeSpan duration,
        bool cancelled)
    {
        var tags = Tags(
            tenant,
            agent,
            cancelled ? "cancelled" : "failed",
            definition?.Agent.Provider,
            definition?.Provider.ModelName);
        _runs.Add(1, tags);
        _runDuration.Record(duration.TotalMilliseconds, tags);
    }

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
