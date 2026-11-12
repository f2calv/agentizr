namespace CasCap.Client.Models;

/// <summary>Selects which published versions the administration history lists.</summary>
public enum DefinitionHistoryFilter
{
    /// <summary>Versions that are not archived.</summary>
    Current,

    /// <summary>Archived versions only.</summary>
    Archived,

    /// <summary>Every version.</summary>
    All,
}
