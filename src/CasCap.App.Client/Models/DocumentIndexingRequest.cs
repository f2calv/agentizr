namespace CasCap.Client.Models;

/// <summary>Explicitly confirms one exact preflight and logical submission/input-token ceilings, not a monetary guarantee.</summary>
public sealed record DocumentIndexingRequest
{
    /// <summary>Gets the exact currently eligible preflight identity.</summary>
    /// <example>0000000000000000000000000000000000000000000000000000000000000000</example>
    [Required, RegularExpression("^[A-F0-9]{64}$")]
    public required string ManifestSha256 { get; init; }

    /// <summary>Gets the explicit authorization to submit embedding inputs and publish a complete current generation.</summary>
    /// <example>false</example>
    public required bool Confirmed { get; init; }

    /// <summary>Gets the maximum logical provider submissions; unknown outcomes never consume an automatic retry.</summary>
    /// <example>2</example>
    [Range(1, 5_000)]
    public required int MaximumRequests { get; init; }

    /// <summary>Gets the input-token allowance; provider billing can differ and cannot be undone.</summary>
    /// <example>10000</example>
    [Range(1, 100_000_000)]
    public required int MaximumInputTokens { get; init; }

    /// <summary>Gets the observed active generation, or explicit null before first publication.</summary>
    /// <example>null</example>
    [MaxLength(64)]
    public required string? ExpectedActiveJobId { get; init; }
}
