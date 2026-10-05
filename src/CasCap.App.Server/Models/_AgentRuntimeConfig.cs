namespace CasCap.Models;

/// <summary>Configuration-backed bootstrap definitions for the multi-tenant agent runtime.</summary>
public sealed record AgentRuntimeConfig : IAppConfig
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(AgentRuntimeConfig)}";

    /// <summary>Gets the tenant used until authenticated request claims become the tenant authority.</summary>
    [Required, MinLength(1)]
    public string DefaultTenantId { get; init; } = "default";

    /// <summary>Gets tenant definitions keyed by stable tenant identifier.</summary>
    public Dictionary<string, TenantAgentConfig> Tenants { get; init; } = [];
}

/// <summary>Versioned provider and agent definitions for one tenant.</summary>
public sealed record TenantAgentConfig
{
    /// <summary>Gets the definition version used to invalidate built-agent caches.</summary>
    [Required, MinLength(1)]
    public string DefinitionVersion { get; init; } = "1";

    /// <summary>Gets providers keyed by stable provider name.</summary>
    public Dictionary<string, ProviderConfig> Providers { get; init; } = [];

    /// <summary>Gets agents keyed by stable agent name.</summary>
    public Dictionary<string, AgentConfig> Agents { get; init; } = [];
}
