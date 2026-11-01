namespace CasCap.Client.Models;

/// <summary>Records snapshot-local inclusion decisions without approving indexing.</summary>
public sealed record DocumentChunkReviewRequest : IValidatableObject
{
    /// <summary>Gets the observed source ordering token.</summary>
    /// <example>0000000000000001</example>
    [Required, StringLength(128, MinimumLength = 1)]
    public required string Sequencer { get; init; }

    /// <summary>Gets the observed source revision.</summary>
    /// <example>"example-etag"</example>
    [Required, StringLength(1_024, MinimumLength = 1)]
    public required string ETag { get; init; }

    /// <summary>Gets the observed review revision, or zero when no review exists.</summary>
    /// <example>0</example>
    public required long ExpectedRevision { get; init; }

    /// <summary>Gets excluded candidates; every other candidate remains selected for future review gates.</summary>
    /// <example>[]</example>
    [Required, MaxLength(10_000)]
    public required IReadOnlyList<DocumentChunkExclusionRequest> Exclusions { get; init; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ExpectedRevision is < 0 or long.MaxValue)
            yield return new("The observed review revision must allow a nonnegative successor.", [nameof(ExpectedRevision)]);
        if (Exclusions is null)
            yield break;
        if (Exclusions.Any(item => item is null || item.Ordinal < 0 || item.Reason?.Length > 500)
            || Exclusions.Select(item => item?.Ordinal).Distinct().Count() != Exclusions.Count)
            yield return new("Exclusions must contain distinct valid candidate ordinals and bounded reasons.", [nameof(Exclusions)]);
    }
}
