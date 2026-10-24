namespace CasCap.Models;

/// <summary>One page of agent summaries and the total number of matching agents.</summary>
/// <param name="Items">The agents on this page, ordered by name.</param>
/// <param name="TotalCount">How many agents match across every page.</param>
public sealed record AgentDefinitionSummaryPage(IReadOnlyList<AgentDefinitionSummaryItem> Items, int TotalCount);
