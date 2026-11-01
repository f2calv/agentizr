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

    /// <summary>Gets whether durable processing is enabled; requires a real registered document processor.</summary>
    public bool ProcessingEnabled { get; init; }

    /// <summary>Gets whether startup/hourly synchronization and administrator requests are enabled.</summary>
    /// <remarks>Enable after the additive migration and compatible ingestion replicas are deployed.</remarks>
    public bool SynchronizationEnabled { get; init; }

    /// <summary>Gets the five-field UTC schedule used by <see cref="DocumentIngestionFeature"/>.</summary>
    [Required, MinLength(1), MaxLength(200)]
    public string SynchronizationCron { get; init; } = "0 * * * *";

    /// <summary>Gets the cooperative per-source deadline used by <see cref="DocumentSynchronizationService"/>.</summary>
    [Range(30, 3_600)]
    public int SynchronizationTimeoutSeconds { get; init; } = 600;

    /// <summary>Gets the attempt deadline in seconds, used by <see cref="DocumentIngestionJobService"/>
    /// and <see cref="DocumentIngestionProcessingService"/>.</summary>
    [Range(30, 900)]
    public int ProcessingTimeoutSeconds { get; init; } = 300;

    /// <summary>Gets explicit container-to-tenant/collection mappings.</summary>
    public List<DocumentSourceConfig> Sources { get; init; } = [];

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Cronos.CronExpression.TryParse(SynchronizationCron, out var schedule)
            || schedule.GetNextOccurrence(DateTime.UnixEpoch) is null)
            yield return new ValidationResult("SynchronizationCron must describe a recurring five-field UTC schedule.");
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
            if (source.AgentPreparation is { } preparation)
            {
                results.Clear();
                Validator.TryValidateObject(preparation, new ValidationContext(preparation), results, validateAllProperties: true);
                foreach (var result in results)
                    yield return result;
            }
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

    /// <summary>Gets explicit hybrid preparation authorization; null retains the existing local-only PDF path.</summary>
    public DocumentAgentPreparationConfig? AgentPreparation { get; init; }
}

/// <summary>Operator-selected hybrid binding and paid-attempt budgets; no chargeable defaults are selected.</summary>
public sealed record DocumentAgentPreparationConfig : IValidatableObject
{
    /// <summary>Gets the tenant-local agent key.</summary>
    [Required, MinLength(1), MaxLength(200)]
    public string AgentName { get; init; } = string.Empty;

    /// <summary>Gets the exact approved active version to pin at admission.</summary>
    [Required, MinLength(1), MaxLength(100)]
    public string DefinitionVersion { get; init; } = string.Empty;

    /// <summary>Gets the expected public base model identifier.</summary>
    [Required, MinLength(1), MaxLength(200)]
    public string Model { get; init; } = string.Empty;

    /// <summary>Gets the exact model revision to verify against deployment metadata before paid submissions.</summary>
    [Required, MinLength(1), MaxLength(100)]
    public string ModelRevision { get; init; } = string.Empty;

    /// <summary>Gets the deployment ARM resource ID used for read-only revision verification.</summary>
    [Required, MinLength(1), MaxLength(2_000)]
    public string DeploymentResourceId { get; init; } = string.Empty;

    /// <summary>Gets the explicitly selected Azure cloud's credential-free Resource Manager HTTPS root.</summary>
    [Required, Url]
    public string ManagementEndpoint { get; init; } = string.Empty;

    /// <summary>Gets the credential-free Document Intelligence HTTPS root endpoint.</summary>
    [Required, Url]
    public string DocumentIntelligenceEndpoint { get; init; } = string.Empty;

    /// <summary>Gets the maximum selected-page count; without a selection this bounds the whole document.</summary>
    [Range(1, 200)]
    public int MaximumPages { get; init; }

    /// <summary>Gets optional one-based original physical pages; empty processes every page, without renumbering or replacing the source.</summary>
    [Required, MaxLength(200)]
    public int[] PhysicalPages { get; init; } = [];

    /// <summary>Gets the per-page output-token ceiling, including reasoning tokens.</summary>
    [Range(1, 8_192)]
    public int MaximumOutputTokens { get; init; }

    /// <summary>Gets the per-request deadline within the existing durable attempt deadline.</summary>
    [Range(1, 600)]
    public int RequestTimeoutSeconds { get; init; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PhysicalPages.Length > MaximumPages || PhysicalPages.Any(page => page is < 1 or > 200)
            || PhysicalPages.Distinct().Count() != PhysicalPages.Length)
            yield return new ValidationResult("Preparation pages must be distinct physical numbers from 1 to 200 within the selected-page budget.");
        if (!Uri.TryCreate(DocumentIntelligenceEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps || endpoint.Port != 443 || endpoint.AbsolutePath != "/"
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            yield return new ValidationResult("Hybrid preparation requires a credential-free HTTPS Document Intelligence root endpoint.");
        if (!Uri.TryCreate(ManagementEndpoint, UriKind.Absolute, out var management)
            || management.Scheme != Uri.UriSchemeHttps || management.Port != 443 || management.AbsolutePath != "/"
            || management.UserInfo.Length != 0 || management.Query.Length != 0 || management.Fragment.Length != 0
            || management.Host is not ("management.azure.com" or "management.usgovcloudapi.net" or "management.chinacloudapi.cn"))
            yield return new ValidationResult("Hybrid preparation requires a supported Azure Resource Manager HTTPS root.");
        var segments = (DeploymentResourceId ?? string.Empty).Split('/');
        if (segments.Length != 11 || segments[0].Length != 0
            || !string.Equals(segments[1], "subscriptions", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(segments[2], out _)
            || !string.Equals(segments[3], "resourceGroups", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[5], "providers", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[6], "Microsoft.CognitiveServices", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[7], "accounts", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[9], "deployments", StringComparison.OrdinalIgnoreCase)
            || segments.Any(segment => segment is "." or ".." || segment.Contains('?') || segment.Contains('#') || segment.Contains('%'))
            || string.IsNullOrWhiteSpace(segments.ElementAtOrDefault(4))
            || string.IsNullOrWhiteSpace(segments.ElementAtOrDefault(8))
            || string.IsNullOrWhiteSpace(segments.ElementAtOrDefault(10)))
            yield return new ValidationResult("Hybrid preparation requires an exact Cognitive Services deployment resource ID.");
    }
}
