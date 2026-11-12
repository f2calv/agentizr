namespace CasCap.Models;

/// <summary>Selects which published versions a history page lists.</summary>
public enum AgentDefinitionHistoryFilter
{
    /// <summary>Versions that are not archived.</summary>
    Current,

    /// <summary>Archived versions only.</summary>
    Archived,

    /// <summary>Every version.</summary>
    All,
}
