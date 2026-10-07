namespace CasCap.Constants;

/// <summary>OAuth scope values accepted by Agent Runtime control-plane policies.</summary>
public static class AgentRuntimeScopes
{
    /// <summary>Allows reading definition documents and history.</summary>
    public const string DefinitionRead = "agent.definition.read";

    /// <summary>Allows publishing immutable definition snapshots.</summary>
    public const string DefinitionPublish = "agent.definition.publish";

    /// <summary>Allows activating or rolling back definition versions.</summary>
    public const string DefinitionActivate = "agent.definition.activate";
}
