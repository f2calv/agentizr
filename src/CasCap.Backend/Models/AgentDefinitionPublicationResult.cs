using CasCap.AgentRuntime.Contracts.V1;

namespace CasCap.Models;

/// <summary>Publication outcome with a version 1 snapshot DTO, independent of HTTP result types.</summary>
public sealed record AgentDefinitionPublicationResult
{
    /// <summary>Gets the published snapshot on success.</summary>
    public AgentDefinitionSnapshotResponse? Snapshot { get; init; }

    /// <summary>Gets a public-safe validation error.</summary>
    public string? ValidationError { get; init; }

    /// <summary>Gets whether the requested version already exists.</summary>
    public bool IsConflict { get; init; }

    /// <summary>Gets whether the definition authority supports publication.</summary>
    public bool IsSupported { get; init; }
}
