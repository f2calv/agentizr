#if DEBUG
using CasCap.Client.Constants;

namespace CasCap.Models;

/// <summary>Operator-owned indexing eligibility; enabling preflight never authorizes a paid operation.</summary>
public sealed record DocumentIndexingConfig : IAppConfig, IValidatableObject
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(DocumentIndexingConfig)}";

    /// <summary>Gets whether configured indexing eligibility may be evaluated.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets explicit immutable-profile inputs; no provider or model is selected by default.</summary>
    [Required, MaxLength(32)]
    public List<DocumentEmbeddingProfileConfig> Profiles { get; init; } = [];

    /// <summary>Gets trusted tenant-qualified collection classifications, not reviewer overrides.</summary>
    [Required, MaxLength(1_000)]
    public List<DocumentIndexingCollectionConfig> Collections { get; init; } = [];

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Profiles is null || Collections is null)
            yield break;
        if (Enabled && (Profiles.Count == 0 || Collections.Count == 0))
            yield return new("Enabled indexing preflight requires explicit profiles and collection policies.");
        var profiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in Profiles)
        {
            if (profile is null)
            {
                yield return new("Embedding profiles cannot contain null entries.");
                continue;
            }
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(profile, new ValidationContext(profile), results, validateAllProperties: true);
            foreach (var result in results)
                yield return result;
            if (!profiles.Add(profile.Id))
                yield return new("Embedding profile IDs must be unique.");
        }
        var collections = new HashSet<(string, string)>();
        foreach (var collection in Collections)
        {
            if (collection is null)
            {
                yield return new("Collection policies cannot contain null entries.");
                continue;
            }
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(collection, new ValidationContext(collection), results, validateAllProperties: true);
            foreach (var result in results)
                yield return result;
            if (!collections.Add((collection.TenantId, collection.CollectionId)))
                yield return new("A tenant collection can have only one indexing policy.");
            if (!profiles.Contains(collection.ProfileId))
                yield return new("Every collection policy must reference a configured embedding profile.");
        }
    }
}

/// <summary>Pins model compatibility and input bounds without resolving credentials or submitting embeddings.</summary>
public sealed record DocumentEmbeddingProfileConfig
{
    /// <summary>Gets the operator's logical profile name; content is additionally hash-bound.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the tenant-authorized provider registration name, not a credential.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string Provider { get; init; } = string.Empty;

    /// <summary>Gets the exact embedding model identifier.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string Model { get; init; } = string.Empty;

    /// <summary>Gets the explicitly pinned model revision.</summary>
    [Required, StringLength(100, MinimumLength = 1)]
    public string ModelRevision { get; init; } = string.Empty;

    /// <summary>Gets expected vector dimensions; the storage adapter must separately verify support.</summary>
    [Range(1, 16_000)]
    public int Dimensions { get; init; }

    /// <summary>Gets the first supported distance metric.</summary>
    [Required, RegularExpression("^cosine$")]
    public string DistanceMetric { get; init; } = string.Empty;

    /// <summary>Gets the tokenizer encoding required for the captured input token counts.</summary>
    [Required, StringLength(100, MinimumLength = 1)]
    public string TokenizerEncoding { get; init; } = string.Empty;

    /// <summary>Gets the exact tokenizer library version required by the snapshot.</summary>
    [Required, StringLength(100, MinimumLength = 1)]
    public string TokenizerVersion { get; init; } = string.Empty;

    /// <summary>Gets the maximum complete heading-aware input tokens per vector.</summary>
    [Range(1, 100_000)]
    public int MaximumTokensPerInput { get; init; }

    /// <summary>Gets the maximum included chunks for this bounded indexing request.</summary>
    [Range(1, 5_000)]
    public int MaximumChunks { get; init; }

    /// <summary>Gets the total input token ceiling; this is not monetary or paid-operation approval.</summary>
    [Range(1, 100_000_000)]
    public int MaximumTotalTokens { get; init; }
}

/// <summary>Trusted single-classification collection policy; mixed-type collections need a stronger trusted classification boundary.</summary>
public sealed record DocumentIndexingCollectionConfig : IValidatableObject
{
    /// <summary>Gets the exact server-owned tenant identity.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Gets the tenant-local collection identity.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string CollectionId { get; init; } = string.Empty;

    /// <summary>Gets the operator-maintained policy revision, also bound to a complete content hash.</summary>
    [Range(1, int.MaxValue)]
    public int Revision { get; init; }

    /// <summary>Gets the one permitted trusted document classification.</summary>
    [Required, MaxLength(40)]
    public string DocumentType { get; init; } = string.Empty;

    /// <summary>Gets the permitted content language.</summary>
    [Required, RegularExpression("^[a-z]{2}$")]
    public string Language { get; init; } = string.Empty;

    /// <summary>Gets the highest permitted reviewed content level, required for learning collections.</summary>
    [MaxLength(2)]
    public string? MaximumCefrLevel { get; init; }

    /// <summary>Gets the exact profile reference.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string ProfileId { get; init; } = string.Empty;

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!DocumentApprovalValues.DocumentTypes.Contains(DocumentType, StringComparer.Ordinal))
            yield return new("A supported trusted document classification is required.");
        if (MaximumCefrLevel is not null
            && !DocumentApprovalValues.CefrLevels.Contains(MaximumCefrLevel, StringComparer.Ordinal))
            yield return new("A supported maximum CEFR level is required.");
        if (DocumentType is DocumentApprovalValues.Vocabulary or DocumentApprovalValues.ExamPaper && MaximumCefrLevel is null)
            yield return new("Learning collections require an explicit maximum CEFR level.");
    }
}
#endif
