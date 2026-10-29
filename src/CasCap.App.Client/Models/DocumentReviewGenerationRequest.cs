using System.Text.Json;

namespace CasCap.Client.Models;

/// <summary>Stores a source-fenced Markdown projection without approving content or invoking preparation.</summary>
public sealed record DocumentReviewGenerationRequest : IValidatableObject
{
    /// <summary>Gets the source sequence observed in the review preview.</summary>
    /// <example>0000000000000001</example>
    [Required, StringLength(128, MinimumLength = 1)]
    public required string Sequencer { get; init; }

    /// <summary>Gets the source ETag observed in the review preview.</summary>
    /// <example>"example-etag"</example>
    [Required, StringLength(1_024, MinimumLength = 1)]
    public required string ETag { get; init; }

    /// <summary>Gets the hard token ceiling, including heading ancestry.</summary>
    /// <example>512</example>
    [Range(8, 8_192)]
    public int MaximumTokensPerChunk { get; init; } = 512;

    /// <summary>Gets an optional semantic packing target below the hard ceiling.</summary>
    /// <example>384</example>
    [Range(8, 8_192)]
    public int? TargetTokensPerChunk { get; init; }

    /// <summary>Gets the repeated suffix budget for oversized paragraphs.</summary>
    /// <example>64</example>
    [Range(0, 8_191)]
    public int OverlapTokens { get; init; } = 64;

    /// <summary>Gets optional schema-2 source-role/heading/layout decisions bound to the preview's Markdown digest.</summary>
    /// <remarks>This versioned document contains no authenticated actor or approval authority.</remarks>
    /// <example>null</example>
    public JsonElement? Review { get; init; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OverlapTokens >= MaximumTokensPerChunk)
            yield return new ValidationResult("Overlap must be smaller than the hard token ceiling.", [nameof(OverlapTokens)]);
        if (TargetTokensPerChunk > MaximumTokensPerChunk)
            yield return new ValidationResult("Target must not exceed the hard token ceiling.", [nameof(TargetTokensPerChunk)]);
    }
}
