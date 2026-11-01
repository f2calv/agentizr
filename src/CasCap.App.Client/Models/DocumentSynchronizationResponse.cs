namespace CasCap.Client.Models;

/// <summary>Describes configured application sources and synchronization progress without storage coordinates or content.</summary>
public sealed record DocumentSynchronizationResponse
{
    /// <summary>Gets whether durable source synchronization is enabled.</summary>
    /// <example>true</example>
    public bool Enabled { get; init; }

    /// <summary>Gets whether the separate background processing gate is enabled.</summary>
    /// <example>false</example>
    public bool ProcessingEnabled { get; init; }

    /// <summary>Gets the five-field UTC synchronization schedule.</summary>
    /// <example>0 * * * *</example>
    public required string Cron { get; init; }

    /// <summary>Gets progress only for the authenticated application's configured sources.</summary>
    /// <example>[]</example>
    public required IReadOnlyList<DocumentSynchronizationSourceResponse> Sources { get; init; }
}
