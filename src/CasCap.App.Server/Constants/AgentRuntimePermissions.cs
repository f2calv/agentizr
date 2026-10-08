namespace CasCap.Constants;

/// <summary>OAuth permission values accepted from delegated scopes or application roles.</summary>
public static class AgentRuntimePermissions
{
    /// <summary>Allows executing agents and managing their tenant-scoped sessions and overrides.</summary>
    public const string Execute = "agent.execute";

    /// <summary>Allows reading definition documents and history.</summary>
    public const string DefinitionRead = "agent.definition.read";

    /// <summary>Allows publishing immutable definition snapshots.</summary>
    public const string DefinitionPublish = "agent.definition.publish";

    /// <summary>Allows activating or rolling back definition versions.</summary>
    public const string DefinitionActivate = "agent.definition.activate";
}
