namespace CasCap.Constants;

/// <summary>Authorization policy names for the Agent Runtime control plane.</summary>
public static class AgentRuntimePolicies
{
    /// <summary>Allows reading definition documents and history.</summary>
    public const string DefinitionRead = nameof(DefinitionRead);

    /// <summary>Allows publishing immutable definition snapshots.</summary>
    public const string DefinitionPublish = nameof(DefinitionPublish);

    /// <summary>Allows activating or rolling back definition versions.</summary>
    public const string DefinitionActivate = nameof(DefinitionActivate);
}
