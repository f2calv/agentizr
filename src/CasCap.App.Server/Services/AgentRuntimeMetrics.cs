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
            description: "Tool calls completed during Agent Runtime executions.");
    }

    /// <summary>Records a run whose requested definition was unavailable.</summary>
    public void RecordUnavailable(string tenant, string agent, TimeSpan duration)
    {
        var tags = Tags(tenant, agent, outcome: "unavailable");
        _runs.Add(1, tags);
        _runDuration.Record(duration.TotalMilliseconds, tags);
    }

    /// <summary>Records a completed run and its provider diagnostics.</summary>
    public void RecordCompleted(
        string tenant,
        AgentDefinition definition,
        string model,
        TimeSpan duration,
        AgentRunResult? diagnostics)
    {
        var tags = Tags(tenant, definition.Name, "succeeded", definition.Agent.Provider, model);
        _runs.Add(1, tags);
        _runDuration.Record(duration.TotalMilliseconds, tags);
        if (diagnostics?.TimeToFirstToken is { } firstToken)
            _timeToFirstToken.Record(firstToken.TotalMilliseconds, tags);
        if (diagnostics?.ToolCallCount > 0)
            _toolCalls.Add(diagnostics.ToolCallCount, tags);
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
