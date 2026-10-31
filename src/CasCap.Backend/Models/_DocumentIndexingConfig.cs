#if DEBUG
using CasCap.Client.Constants;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CasCap.Models;

/// <summary>Operator-owned indexing eligibility; enabling preflight never authorizes a paid operation.</summary>
public sealed record DocumentIndexingConfig : IAppConfig, IValidatableObject
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(DocumentIndexingConfig)}";

    /// <summary>Gets whether configured indexing eligibility may be evaluated.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets whether the ingestion role may claim separately authorized jobs; disabled by default.</summary>
    public bool ProcessingEnabled { get; init; }

    /// <summary>Gets the database claim and per-batch cooperative deadline.</summary>
    [Range(30, 900)]
    public int ProcessingTimeoutSeconds { get; init; } = 300;

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
        if (ProcessingEnabled && !Enabled)
            yield return new("Indexing processing requires enabled eligibility checks.");
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
            if (profile.Azure is { } azure)
            {
                results.Clear();
                Validator.TryValidateObject(azure, new ValidationContext(azure), results, validateAllProperties: true);
                foreach (var result in results)
                    yield return result;
                var maximumDimensions = profile.Model switch
                {
                    "text-embedding-3-small" => 1_536,
                    "text-embedding-3-large" => 3_072,
                    _ => 0,
                };
                if (maximumDimensions == 0 || profile.Dimensions > maximumDimensions
                    || profile.TokenizerEncoding != "cl100k_base" || profile.MaximumTokensPerInput > 8_192)
                    yield return new("The Azure adapter requires a compatible text-embedding-3 model, dimensions and cl100k_base input ceiling.");
            }
            if (profile.Ollama is { } ollama)
            {
                results.Clear();
                Validator.TryValidateObject(ollama, new ValidationContext(ollama), results, validateAllProperties: true);
                foreach (var result in results)
                    yield return result;
                if (string.IsNullOrEmpty(profile.ModelRevision)
                    || !Regex.IsMatch(profile.ModelRevision, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant)
                    || string.IsNullOrEmpty(profile.Model)
                    || !Regex.IsMatch(profile.Model, "^[a-z0-9][a-z0-9._/-]*:[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant))
                    yield return new("Ollama embeddings require an explicit model tag and full lowercase SHA256 model digest.");
            }
            if (profile.Azure is not null && profile.Ollama is not null)
                yield return new("An embedding profile cannot select both Azure and Ollama.");
            if (ProcessingEnabled && !profile.HasExecutionBinding)
                yield return new("Indexing processing requires exactly one explicit embedding provider binding.");
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

    /// <summary>Gets the maximum inputs in one non-retried provider submission.</summary>
    [Range(1, 32)]
    public int MaximumBatchInputs { get; init; } = 16;

    /// <summary>Gets optional Azure deployment metadata; exactly one provider binding is required for execution.</summary>
    public DocumentAzureEmbeddingConfig? Azure { get; init; }

    /// <summary>Gets an optional local-model Ollama binding, independent of the tenant's chat model.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentOllamaEmbeddingConfig? Ollama { get; init; }

    /// <summary>Gets whether exactly one executable provider binding is selected.</summary>
    internal bool HasExecutionBinding => (Azure is null) != (Ollama is null);
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

/// <summary>Explicit credential-free Azure OpenAI and Resource Manager binding, never inferred from chat defaults.</summary>
public sealed record DocumentAzureEmbeddingConfig : IValidatableObject
{
    /// <summary>Gets the authorized Azure OpenAI HTTPS root.</summary>
    [Required, Url]
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Gets the exact embedding deployment name.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string DeploymentName { get; init; } = string.Empty;

    /// <summary>Gets the exact model deployment ARM resource ID.</summary>
    [Required, StringLength(2_000, MinimumLength = 1)]
    public string DeploymentResourceId { get; init; } = string.Empty;

    /// <summary>Gets the explicitly selected Azure cloud's Resource Manager HTTPS root.</summary>
    [Required, Url]
    public string ManagementEndpoint { get; init; } = string.Empty;

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var endpoint in new[] { Endpoint, ManagementEndpoint })
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443
                || uri.AbsolutePath != "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                yield return new("Embedding and management endpoints must be credential-free HTTPS roots.");
        if (Uri.TryCreate(ManagementEndpoint, UriKind.Absolute, out var management)
            && management.Host is not ("management.azure.com" or "management.usgovcloudapi.net" or "management.chinacloudapi.cn"))
            yield return new("Embedding deployment verification requires a recognized Azure Resource Manager authority.");
        var segments = DeploymentResourceId.Split('/');
        if (segments.Length != 11 || segments[0].Length != 0 || segments[1] != "subscriptions"
            || !Guid.TryParse(segments[2], out _) || segments[3] != "resourceGroups"
            || string.IsNullOrWhiteSpace(segments[4]) || segments[5] != "providers"
            || segments[6] != "Microsoft.CognitiveServices" || segments[7] != "accounts"
            || string.IsNullOrWhiteSpace(segments[8]) || segments[9] != "deployments"
            || segments[10] != DeploymentName
            || segments.Any(segment => segment.Contains('%') || segment.Contains('?') || segment.Contains('#')))
            yield return new("Embedding deployment metadata must identify the exact configured Azure deployment.");
    }
}

/// <summary>Explicit local-model Ollama endpoint, still subject to exact tenant registration and authority policy.</summary>
public sealed record DocumentOllamaEmbeddingConfig : IValidatableObject
{
    /// <summary>Gets the credential-free HTTP or HTTPS root; no model is downloaded or selected implicitly.</summary>
    [Required, Url]
    public string Endpoint { get; init; } = string.Empty;

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.AbsolutePath != "/" || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            yield return new("Ollama embeddings require a credential-free HTTP or HTTPS root.");
    }
}
#endif
