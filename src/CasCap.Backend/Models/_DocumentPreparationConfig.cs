namespace CasCap.Models;

/// <summary>Startup resource limits for local PDF preparation, independent of model-provider credentials.</summary>
public sealed record DocumentPreparationConfig : IAppConfig, IValidatableObject
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(DocumentPreparationConfig)}";

    /// <summary>Gets the maximum source bytes read before rejecting a PDF.</summary>
    [Range(1, 104_857_600)]
    public int MaximumPdfBytes { get; init; } = 26_214_400;

    /// <summary>Gets the maximum admitted page count.</summary>
    [Range(1, 2_000)]
    public int MaximumPages { get; init; } = 200;

    /// <summary>Gets the maximum extracted UTF-16 character count across all pages.</summary>
    [Range(1, 20_000_000)]
    public int MaximumExtractedCharacters { get; init; } = 2_000_000;

    /// <summary>Gets the maximum output chunk count; exceeding it rejects the whole preparation.</summary>
    [Range(1, 100_000)]
    public int MaximumChunks { get; init; } = 5_000;

    /// <summary>Gets the default chunking settings, overridable explicitly for each preparation call.</summary>
    public DocumentChunkingConfig Chunking { get; init; } = new();

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Chunking is null)
            return [new ValidationResult("Default chunking settings are required.")];
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(Chunking, new ValidationContext(Chunking), results, validateAllProperties: true);
        return results;
    }
}

/// <summary>A per-call token budget; it must later be validated against the selected embedding profile.</summary>
public sealed record DocumentChunkingConfig : IValidatableObject
{
    /// <summary>Gets the maximum actual cl100k_base tokens per chunk, not a character approximation.</summary>
    [Range(8, 8_192)]
    public int MaximumTokensPerChunk { get; init; } = 512;

    /// <summary>Gets the maximum suffix-token overlap between consecutive chunks of the same page.</summary>
    [Range(0, 8_191)]
    public int OverlapTokens { get; init; } = 64;

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OverlapTokens >= MaximumTokensPerChunk)
            yield return new ValidationResult("Chunk overlap must be smaller than the chunk token budget.");
    }

}

/// <summary>Explicit budgets for offline heading-aware Markdown projections; enables no ingestion or indexing.</summary>
public sealed record DocumentMarkdownChunkingConfig : IValidatableObject
{
    /// <summary>Gets the hard token ceiling including the complete heading prefix and body.</summary>
    [Range(8, 8_192)]
    public int MaximumTokensPerChunk { get; init; } = 512;

    /// <summary>Gets a soft packing target; null uses the hard ceiling without splitting otherwise fitting semantic units.</summary>
    [Range(8, 8_192)]
    public int? TargetTokensPerChunk { get; init; }

    /// <summary>Gets the maximum repeated suffix tokens when splitting an oversized paragraph.</summary>
    [Range(0, 8_191)]
    public int OverlapTokens { get; init; } = 64;

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OverlapTokens >= MaximumTokensPerChunk)
            yield return new ValidationResult("Markdown overlap must be smaller than the hard token ceiling.");
        if (TargetTokensPerChunk > MaximumTokensPerChunk)
            yield return new ValidationResult("Markdown target must not exceed the hard token ceiling.");
    }
}
