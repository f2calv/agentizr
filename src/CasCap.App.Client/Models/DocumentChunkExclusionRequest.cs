namespace CasCap.Client.Models;

/// <summary>Excludes one snapshot-local candidate without changing its source text.</summary>
public sealed record DocumentChunkExclusionRequest
{
    /// <summary>Gets the zero-based candidate ordinal.</summary>
    /// <example>0</example>
    [Range(0, int.MaxValue)]
    public required int Ordinal { get; init; }

    /// <summary>Gets an optional reviewer explanation, never embedding input.</summary>
    /// <example>Irrelevant publisher advertisement.</example>
    [StringLength(500)]
    public string? Reason { get; init; }
}
