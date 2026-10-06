namespace CasCap.Models;

/// <summary>Audit projection for one immutable agent-definition snapshot.</summary>
public sealed record AgentDefinitionHistoryItem
{
    /// <summary>Gets the definition version.</summary>
    public required string DefinitionVersion { get; init; }

    /// <summary>Gets the JSON schema version.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Gets when the snapshot was published.</summary>
    public DateTimeOffset PublishedAtUtc { get; init; }

    /// <summary>Gets the publisher identifier.</summary>
    public string? PublishedBy { get; init; }

    /// <summary>Gets the publication reason.</summary>
    public string? ChangeReason { get; init; }
}
