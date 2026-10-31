namespace CasCap.Constants;

/// <summary>Authorization policy names for Agent Runtime execution and control-plane operations.</summary>
public static class AgentRuntimePolicies
{
    /// <summary>Allows executing agents and managing their tenant-scoped sessions and overrides.</summary>
    public const string Execute = nameof(Execute);

    /// <summary>Allows reading definition documents and history.</summary>
    public const string DefinitionRead = nameof(DefinitionRead);

    /// <summary>Allows publishing immutable definition snapshots.</summary>
    public const string DefinitionPublish = nameof(DefinitionPublish);

    /// <summary>Allows activating or rolling back definition versions.</summary>
    public const string DefinitionActivate = nameof(DefinitionActivate);
}
