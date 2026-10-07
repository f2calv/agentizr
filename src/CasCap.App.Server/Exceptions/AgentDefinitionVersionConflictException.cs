namespace CasCap.Exceptions;

/// <summary>Indicates that an immutable tenant definition version already exists.</summary>
public sealed class AgentDefinitionVersionConflictException : Exception
{
    /// <summary>Initializes a duplicate-version conflict.</summary>
    public AgentDefinitionVersionConflictException()
        : base("The definition version already exists and is immutable.")
    {
    }

    /// <summary>Initializes a duplicate-version conflict with its persistence cause.</summary>
    public AgentDefinitionVersionConflictException(Exception innerException)
        : base("The definition version already exists and is immutable.", innerException)
    {
    }
}
