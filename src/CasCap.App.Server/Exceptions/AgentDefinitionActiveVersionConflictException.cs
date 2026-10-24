namespace CasCap.Exceptions;

/// <summary>Indicates that another decision changed an agent's active definition since the caller observed it.</summary>
public sealed class AgentDefinitionActiveVersionConflictException : Exception
{
    /// <summary>Initializes a stale active-version conflict.</summary>
    public AgentDefinitionActiveVersionConflictException()
        : base("The active definition version changed since it was observed.")
    {
    }
}
