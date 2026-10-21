namespace CasCap.Models;

/// <summary>Configuration-backed bootstrap definitions for the multi-tenant agent runtime.</summary>
public sealed record AgentRuntimeConfig : IAppConfig, IValidatableObject
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(AgentRuntimeConfig)}";

    /// <summary>Gets the tenant used until authenticated request claims become the tenant authority.</summary>
    [Required, MinLength(1)]
    public string DefaultTenantId { get; init; } = "default";

    /// <summary>Gets tenant definitions keyed by stable tenant identifier.</summary>
    public Dictionary<string, TenantAgentConfig> Tenants { get; init; } = [];

    /// <summary>Gets the sliding expiry in hours for session and override state.</summary>
    /// <remarks>Used by Redis-backed runtime state stores. Defaults to seven days.</remarks>
    [Range(1, 24 * 365)]
    public int StateSlidingExpirationHours { get; init; } = 24 * 7;

    /// <summary>Gets the Redis active-definition cache duration in minutes.</summary>
    [Range(1, 24 * 60)]
    public int DefinitionCacheExpirationMinutes { get; init; } = 60;

    /// <summary>Gets the absolute lifetime in minutes of a cached active-version pointer.</summary>
    /// <remarks>
    /// Activation removes the pointer immediately; this bounds staleness only if that removal is lost,
    /// and is how long runs survive a PostgreSQL outage.
    /// </remarks>
    [Range(1, 24 * 60)]
    public int ActiveVersionCacheExpirationMinutes { get; init; } = 10;

    /// <summary>
    /// Gets the maximum tool invocations, including delegations, across one run and all of its sub-agents.
    /// Further calls are refused with an error result so the model answers with what it has.
    /// </summary>
    [Range(1, 1_000)]
    public int MaximumToolInvocationsPerRun { get; init; } = 50;

    /// <summary>Gets session compaction policies keyed by provider name, shared by every tenant.</summary>
    /// <remarks>
    /// A tenant entry in <see cref="TenantAgentConfig.SessionCompaction"/> replaces the entry here; a
    /// provider with neither uses the <see cref="SessionCompactionConfig"/> defaults.
    /// </remarks>
    public Dictionary<string, SessionCompactionConfig> SessionCompaction { get; init; } = [];

    /// <summary>Resolves the session compaction policy for one tenant's provider.</summary>
    public SessionCompactionConfig GetSessionCompaction(string tenantId, string providerName) =>
        Tenants.TryGetValue(tenantId, out var tenant)
            && tenant.SessionCompaction.TryGetValue(providerName, out var tenantPolicy)
                ? tenantPolicy
                : SessionCompaction.GetValueOrDefault(providerName) ?? new SessionCompactionConfig();

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var policies = SessionCompaction.Select(entry => ($"{nameof(SessionCompaction)}:{entry.Key}", entry.Value))
            .Concat(Tenants.SelectMany(tenant => tenant.Value.SessionCompaction.Select(entry =>
                ($"{nameof(Tenants)}:{tenant.Key}:{nameof(TenantAgentConfig.SessionCompaction)}:{entry.Key}", entry.Value))));
        foreach (var (path, policy) in policies)
        {
            var results = new List<ValidationResult>();
            if (Validator.TryValidateObject(policy, new ValidationContext(policy), results, validateAllProperties: true))
                continue;
            foreach (var result in results)
                yield return new ValidationResult($"{path}: {result.ErrorMessage}", [path]);
        }
    }
}

/// <summary>Versioned provider and agent definitions for one tenant.</summary>
public sealed record TenantAgentConfig
{
    /// <summary>Gets the tenant time-zone identifier used by built-in date/time tools.</summary>
    [Required, MinLength(1)]
    public string TimeZoneId { get; init; } = "UTC";

    /// <summary>
    /// Gets whether every run's user message carries the tenant-local date and time, so agents do not
    /// have to call <c>get_current_datetime_state</c> to know what day it is.
    /// </summary>
    /// <remarks>
    /// On by default. The time travels with the message rather than the instructions, which keeps the
    /// instructions identical between runs for provider prompt caching and dates each stored turn.
    /// </remarks>
    public bool CurrentTimeContextEnabled { get; init; } = true;

    /// <summary>Gets the definition version used to invalidate built-agent caches.</summary>
    [Required, MinLength(1)]
    public string DefinitionVersion { get; init; } = "1";

    /// <summary>Gets providers keyed by stable provider name.</summary>
    public Dictionary<string, ProviderConfig> Providers { get; init; } = [];

    /// <summary>Gets session compaction policies keyed by provider name, replacing the runtime-wide entry.</summary>
    public Dictionary<string, SessionCompactionConfig> SessionCompaction { get; init; } = [];

    /// <summary>
    /// Gets provider names that authenticate with the runtime's own Microsoft Entra identity instead of
    /// an API key, for Azure OpenAI resources with key authentication disabled.
    /// </summary>
    /// <remarks>Defaults to none; each tenant opts in explicitly so no tenant borrows the identity by accident.</remarks>
    public HashSet<string> EntraIdProviders { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets whether execution callers may read the effective instructions of this tenant's agents.
    /// </summary>
    /// <remarks>Off by default; definition reads otherwise require the separate definition-read scope.</remarks>
    public bool InstructionsDisclosureEnabled { get; init; }

    /// <summary>Gets agents keyed by stable agent name.</summary>
    public Dictionary<string, AgentConfig> Agents { get; init; } = [];

    /// <summary>Gets remote MCP credentials keyed by logical definition reference.</summary>
    public Dictionary<string, McpCredentialConfig> McpCredentials { get; init; } = [];
}

/// <summary>Secret-backed Authorization header components for one remote MCP endpoint.</summary>
public sealed record McpCredentialConfig : IValidatableObject
{
    /// <summary>Gets the Authorization scheme, such as Bearer or Basic.</summary>
    [Required, MinLength(1), MaxLength(50)]
    public required string Scheme { get; init; }

    /// <summary>Gets the secret Authorization parameter from the private configuration chain.</summary>
    /// <remarks>
    /// Optional for <c>Basic</c>: when omitted, the parameter is built from the runtime's shared
    /// <c>CasCap:ApiAuthConfig</c> username and password.
    /// </remarks>
    public string? Parameter { get; init; }

    /// <summary>Gets whether this credential uses the Basic scheme.</summary>
    public bool IsBasic => string.Equals(Scheme, "Basic", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IsBasic && string.IsNullOrWhiteSpace(Parameter))
            yield return new ValidationResult(
                $"{nameof(Parameter)} is required unless {nameof(Scheme)} is Basic.",
                [nameof(Parameter)]);
    }
}
