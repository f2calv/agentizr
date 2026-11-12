namespace CasCap.Models;

/// <summary>One newest-first page of an agent's published versions and the total number matching the filter.</summary>
/// <param name="Items">The versions on this page.</param>
/// <param name="TotalCount">How many versions match the filter across every page.</param>
public sealed record AgentDefinitionHistoryPage(IReadOnlyList<AgentDefinitionHistoryItem> Items, int TotalCount);
