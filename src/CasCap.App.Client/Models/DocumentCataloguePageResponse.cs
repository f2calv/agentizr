namespace CasCap.Client.Models;

/// <summary>Returns a bounded page of source/current-job entries ordered by collection, document and job.</summary>
public sealed record DocumentCataloguePageResponse
{
    /// <summary>Gets the entries on this page.</summary>
    /// <example>[]</example>
    public required IReadOnlyList<DocumentCatalogueItemResponse> Items { get; init; }

    /// <summary>Gets the matching entry count.</summary>
    /// <example>2</example>
    public int TotalCount { get; init; }

    /// <summary>Gets the one-based page number.</summary>
    /// <example>1</example>
    public int Page { get; init; }

    /// <summary>Gets the maximum entries per page.</summary>
    /// <example>25</example>
    public int PageSize { get; init; }
}
