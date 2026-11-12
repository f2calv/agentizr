namespace CasCap.Tests.Unit;

/// <summary>Tests the property-level change summary shown for each published definition version.</summary>
[Trait("Category", "Agent Runtime")]
public sealed class AgentDefinitionChangesTests
{
    private const string Base = """
        {"name":"assistant","version":"v1","agent":{"name":"Assistant","prompt":"Hello","maxOutputTokens":100},"provider":{"type":"Ollama","modelName":"small"}}
        """;

    [Fact]
    public void Describe_FirstVersionIsInitial() =>
        Assert.Equal([AgentDefinitionChanges.InitialVersion], AgentDefinitionChanges.Describe(null, Base));

    [Fact]
    public void Describe_IgnoresTheVersionItself() =>
        Assert.Empty(AgentDefinitionChanges.Describe(Base, Base.Replace("\"v1\"", "\"v2\"", StringComparison.Ordinal)));

    [Fact]
    public void Describe_LabelsChangedAgentProviderAndRealtimeProperties()
    {
        const string current = """
            {"name":"assistant","version":"v2","agent":{"name":"Assistant","prompt":"Hi","maxOutputTokens":200},"provider":{"type":"Ollama","modelName":"large"},"realtime":{"voice":"marin"}}
            """;

        Assert.Equal(
            ["default prompt", "max output tokens", "model", "realtime"],
            AgentDefinitionChanges.Describe(Base, current));
    }

    [Fact]
    public void Describe_ReportsRemovedProperties()
    {
        const string current = """
            {"name":"assistant","version":"v2","agent":{"name":"Assistant","prompt":"Hello"},"provider":{"type":"Ollama","modelName":"small"}}
            """;

        Assert.Equal(["max output tokens"], AgentDefinitionChanges.Describe(Base, current));
    }
}
