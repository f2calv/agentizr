namespace CasCap.Client.Models;

/// <summary>Summarizes one agent's active and latest published definitions.</summary>
public sealed record AgentSummaryResponse
{
    /// <summary>Gets the stable app-local agent name.</summary>
    /// <example>assistant</example>
    public required string AgentName { get; init; }

    /// <summary>Gets the active definition version, or <see langword="null" /> when the agent is inactive.</summary>
    /// <example>2026-10-07.1</example>
    public string? ActiveVersion { get; init; }

    /// <summary>Gets the provider type embedded in the active definition.</summary>
    /// <example>AzureOpenAI</example>
    public string? ActiveProviderType { get; init; }

    /// <summary>Gets the model embedded in the active definition.</summary>
    /// <example>gpt-5-mini</example>
    public string? ActiveModelName { get; init; }

    /// <summary>Gets the most recently published definition version.</summary>
    /// <example>2026-10-08.1</example>
    public string? LatestVersion { get; init; }

    /// <summary>Gets when the most recent version was published.</summary>
    /// <example>2026-10-08T09:30:00+00:00</example>
    public DateTimeOffset? LatestPublishedAtUtc { get; init; }

    /// <summary>Gets who published the most recent version.</summary>
    /// <example>00000000-0000-0000-0000-000000000000</example>
    public string? LatestPublishedBy { get; init; }

    /// <summary>Gets how many immutable versions have been published.</summary>
    /// <example>2</example>
    public int VersionCount { get; init; }
}
