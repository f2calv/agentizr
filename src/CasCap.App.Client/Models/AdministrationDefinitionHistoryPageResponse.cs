namespace CasCap.Client.Models;

/// <summary>Returns one newest-first page of an agent's published versions.</summary>
public sealed record AdministrationDefinitionHistoryPageResponse
{
    /// <summary>Gets the versions on this page.</summary>
    public required IReadOnlyList<AdministrationDefinitionVersionResponse> Items { get; init; }

    /// <summary>Gets how many versions match the filter across every page.</summary>
    /// <example>12</example>
    public int TotalCount { get; init; }

    /// <summary>Gets the one-based page number.</summary>
    /// <example>1</example>
    public int Page { get; init; }

    /// <summary>Gets the maximum number of versions per page.</summary>
    /// <example>20</example>
    public int PageSize { get; init; }
}
