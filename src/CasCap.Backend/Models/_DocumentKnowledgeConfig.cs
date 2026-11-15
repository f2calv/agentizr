using CasCap.Client.Constants;

namespace CasCap.Models;

/// <summary>Operator-owned, default-disabled access to published document passages.</summary>
public sealed record DocumentKnowledgeConfig : IAppConfig, IValidatableObject
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(DocumentKnowledgeConfig)}";

    /// <summary>Gets whether protected searches and citation previews are available.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the complete search deadline, including verification, inference and database access.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Gets explicit tenant/agent collection grants; callers cannot select a collection or model.</summary>
    [Required, MaxLength(1_000)]
    public List<DocumentKnowledgeBindingConfig> Bindings { get; init; } = [];

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Bindings is null)
            yield break;
        if (Enabled && Bindings.Count == 0)
            yield return new("Enabled knowledge retrieval requires explicit agent collection bindings.");
        var identities = new HashSet<(string, string)>();
        foreach (var binding in Bindings)
        {
            if (binding is null)
            {
                yield return new("Knowledge bindings cannot contain null entries.");
                continue;
            }
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(binding, new ValidationContext(binding), results, validateAllProperties: true);
            foreach (var result in results)
                yield return result;
            if (!identities.Add((binding.TenantId, binding.AgentName)))
                yield return new("An agent can have only one knowledge collection binding.");
        }
    }
}

/// <summary>Grants one agent a bounded, trusted collection and maximum reviewed learning level.</summary>
public sealed record DocumentKnowledgeBindingConfig : IValidatableObject
{
    /// <summary>Gets the tenant resolved from validated caller claims.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Gets the exact active agent name.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string AgentName { get; init; } = string.Empty;

    /// <summary>Gets the authorized collection; its existing indexing policy selects the embedding profile.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string CollectionId { get; init; } = string.Empty;

    /// <summary>Gets the maximum permitted reviewed document CEFR level.</summary>
    [Required, StringLength(2, MinimumLength = 2)]
    public string MaximumCefrLevel { get; init; } = string.Empty;

    /// <summary>Gets the captured query-token ceiling; every search sends at most one embedding input.</summary>
    [Range(1, 512)]
    public int MaximumQueryTokens { get; init; } = 128;

    /// <summary>Gets whether an explicitly allowlisted realtime call may retrieve one A1 lesson topic.</summary>
    // TODO: Replace the fixed A1 pilot with consumer-owned learner policy before multi-level sessions.
    // A model-writable estimate cannot authorize scope; policy changes must retire old model context.
    public bool RealtimeEnabled { get; init; }

    /// <summary>Gets the maximum lifetime of a realtime call carrying retrieved evidence.</summary>
    [Range(1, 30)]
    public int MaximumRealtimeDurationMinutes { get; init; } = 10;

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!DocumentApprovalValues.CefrLevels.Contains(MaximumCefrLevel, StringComparer.Ordinal))
            yield return new("A supported maximum CEFR level is required.");
        if (RealtimeEnabled && MaximumCefrLevel != "A1")
            yield return new("The bounded realtime pilot requires an operator-owned A1 ceiling.");
    }
}
