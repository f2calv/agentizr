namespace CasCap.Models;

/// <summary>Startup configuration required only by the ingestion role.</summary>
public sealed record DocumentIngestionConfig : IAppConfig, IValidatableObject
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(DocumentIngestionConfig)}";

    /// <summary>Gets the installation-qualified leadership resource shared by all ingestion replicas.</summary>
    [Required, MinLength(1), MaxLength(200)]
    public string LeadershipResource { get; init; } = string.Empty;

    /// <summary>Gets the existing Azure queue endpoint; the runtime never creates queues.</summary>
    [Required, Url]
    public string QueueUri { get; init; } = string.Empty;

    /// <summary>Gets a different private queue for poison messages, provisioned externally.</summary>
    [Required, Url]
    public string PoisonQueueUri { get; init; } = string.Empty;

    /// <summary>Gets whether Event Grid queue bodies are Base64-encoded instead of raw JSON.</summary>
    public bool QueueBase64Encoded { get; init; }

    /// <summary>Gets idle poll delay in milliseconds, used by <see cref="DocumentIngestionFeature"/>.</summary>
    [Range(100, 60_000)]
    public int PollIntervalMs { get; init; } = 2_000;

    /// <summary>Gets admission visibility duration in seconds, used by <see cref="AzureDocumentIngestionQueue"/>.</summary>
    [Range(30, 600)]
    public int VisibilitySeconds { get; init; } = 60;

    /// <summary>Gets explicit container-to-tenant/collection mappings.</summary>
    public List<DocumentSourceConfig> Sources { get; init; } = [];

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IsPrivateEndpoint(QueueUri) || !IsPrivateEndpoint(PoisonQueueUri)
            || string.Equals(QueueUri.TrimEnd('/'), PoisonQueueUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            yield return new ValidationResult("Distinct credential-free HTTPS queue endpoints are required.");
        if (Sources.Count == 0)
            yield return new ValidationResult("At least one explicit document source mapping is required.");
        var containers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in Sources)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(source, new ValidationContext(source), results, validateAllProperties: true);
            foreach (var result in results)
                yield return result;
            if (!IsPrivateEndpoint(source.ContainerUri) || !containers.Add(source.ContainerUri.TrimEnd('/')))
                yield return new ValidationResult("Source containers must be distinct credential-free HTTPS endpoints.");
        }
    }

    private static bool IsPrivateEndpoint(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.UserInfo) && uri.Segments.Length == 2
        && uri.AbsolutePath.Trim('/').Length > 0
        && string.Equals(value.TrimEnd('/'), uri.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal);
}

/// <summary>An operator-authorized source container and its owning application collection.</summary>
public sealed record DocumentSourceConfig
{
    /// <summary>Gets the exact Event Grid storage resource topic.</summary>
    [Required, MinLength(1), MaxLength(2_000)]
    public string Topic { get; init; } = string.Empty;

    /// <summary>Gets the private source container URI without a trailing blob name.</summary>
    [Required, Url]
    public string ContainerUri { get; init; } = string.Empty;

    /// <summary>Gets the trusted application tenant identity.</summary>
    [Required, MinLength(1), MaxLength(200)]
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Gets the tenant-local logical collection identity.</summary>
    [Required, MinLength(1), MaxLength(200)]
    public string CollectionId { get; init; } = string.Empty;
}
