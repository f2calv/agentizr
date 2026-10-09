using Microsoft.Extensions.AI;
using System.Diagnostics.Metrics;

namespace CasCap.Tests.Unit;

/// <summary>Tests the bounded tenant-aware Agent Runtime metric contract.</summary>
[Trait("Category", "Agent Runtime")]
public sealed class AgentRuntimeMetricsTests
{
    [Fact]
    public void RecordCompleted_EmitsTenantDefinitionUsageAndToolMetrics()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var measurements = new List<(string Instrument, long Value, Dictionary<string, object?> Tags)>();
        using var listener = CreateListener(measurements);
        var metrics = new AgentRuntimeMetrics(
            Options.Create(new AppConfig()),
            services.GetRequiredService<IMeterFactory>());
        var definition = new AgentDefinition
        {
            Name = "assistant",
            Version = "v1",
            Agent = new AgentConfig
            {
                Provider = "edge",
                Name = "Assistant",
                Description = "Test assistant",
                Prompt = "Respond.",
            },
            Provider = new ProviderConfig
            {
                Type = AgentType.OpenAI,
                ModelName = "test-model",
            },
        };
        var diagnostics = new AgentRunResult("assistant")
        {
            ToolCallCount = 2,
            Usage = new UsageDetails
            {
                InputTokenCount = 10,
                OutputTokenCount = 4,
                ReasoningTokenCount = 3,
            },
        };

        metrics.RecordCompleted("tenant-a", definition, "override-model", TimeSpan.FromSeconds(1), diagnostics);

        var run = Assert.Single(measurements, measurement => measurement.Instrument == "agentizr.run");
        Assert.Equal(1, run.Value);
        Assert.Equal("tenant-a", run.Tags["tenant"]);
        Assert.Equal("assistant", run.Tags["agent"]);
        Assert.Equal("edge", run.Tags["provider"]);
        Assert.Equal("override-model", run.Tags["model"]);
        Assert.Equal("succeeded", run.Tags["outcome"]);
        Assert.Equal(2, Assert.Single(measurements, measurement => measurement.Instrument == "agentizr.tool.calls").Value);
        Assert.Equal(3, measurements.Count(measurement => measurement.Instrument == "agentizr.tokens"));
    }

    [Fact]
    public void RecordUnavailable_EmitsTenantAndAgentWithoutProviderLabels()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var measurements = new List<(string Instrument, long Value, Dictionary<string, object?> Tags)>();
        using var listener = CreateListener(measurements);
        var metrics = new AgentRuntimeMetrics(
            Options.Create(new AppConfig()),
            services.GetRequiredService<IMeterFactory>());

        metrics.RecordUnavailable("tenant-b", "missing", TimeSpan.FromMilliseconds(10));

        var run = Assert.Single(measurements, measurement => measurement.Instrument == "agentizr.run");
        Assert.Equal("tenant-b", run.Tags["tenant"]);
        Assert.Equal("missing", run.Tags["agent"]);
        Assert.Equal("unavailable", run.Tags["outcome"]);
        Assert.DoesNotContain("provider", run.Tags);
        Assert.DoesNotContain("model", run.Tags);
    }

    private static MeterListener CreateListener(
        List<(string Instrument, long Value, Dictionary<string, object?> Tags)> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "agentizr")
                    meterListener.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            measurements.Add((
                instrument.Name,
                measurement,
                ToDictionary(tags))));
        listener.Start();
        return listener;
    }

    private static Dictionary<string, object?> ToDictionary(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);
        foreach (var tag in tags)
            result.Add(tag.Key, tag.Value);
        return result;
    }
}
