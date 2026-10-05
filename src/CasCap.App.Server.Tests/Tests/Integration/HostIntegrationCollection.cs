namespace CasCap.IntegrationTests;

/// <summary>Serializes in-process hosts that temporarily configure process-wide startup values.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostIntegrationCollection
{
    /// <summary>Collection name used by host integration tests.</summary>
    public const string Name = "Host Integration";
}
