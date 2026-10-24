namespace CasCap.Client.Models;

/// <summary>Returns one page of an app's agents ordered by name.</summary>
public sealed record AgentSummaryPageResponse
{
    /// <summary>Gets the agents on this page.</summary>
    public required IReadOnlyList<AgentSummaryResponse> Items { get; init; }

    /// <summary>Gets how many agents match the filter across every page.</summary>
    /// <example>42</example>
    public int TotalCount { get; init; }

    /// <summary>Gets the one-based page number.</summary>
    /// <example>1</example>
    public int Page { get; init; }

    /// <summary>Gets the maximum number of agents per page.</summary>
    /// <example>25</example>
    public int PageSize { get; init; }
}
