namespace CasCap.Models;

/// <summary>Reports the outcome of archiving, restoring or deleting one published version.</summary>
public enum AgentDefinitionVersionChangeResult
{
    /// <summary>The change was applied, or the version was already in the requested state.</summary>
    Applied,

    /// <summary>The version does not exist.</summary>
    NotFound,

    /// <summary>The version is active, so it cannot be archived or deleted.</summary>
    Active,

    /// <summary>The version was activated at least once, so the audit history keeps it.</summary>
    PreviouslyActivated,

    /// <summary>The definition store is read-only.</summary>
    NotSupported,
}
