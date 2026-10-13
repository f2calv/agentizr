namespace CasCap.Models;

/// <summary>Security policy for tenant-published Agent Runtime definitions.</summary>
public sealed record AgentDefinitionPolicyConfig : IAppConfig
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(AgentDefinitionPolicyConfig)}";

    /// <summary>Gets supported JSON document schema versions.</summary>
    [MinLength(1)]
    public int[] SupportedSchemaVersions { get; init; } = [1];

    /// <summary>Gets provider URI authorities permitted in published definitions.</summary>
    public string[] AllowedProviderAuthorities { get; init; } = ["http://localhost:11434"];

    /// <summary>Gets remote MCP URI authorities permitted in published definitions.</summary>
    public string[] AllowedMcpAuthorities { get; init; } = [];

    /// <summary>Gets in-process service tool names permitted in published definitions.</summary>
    public string[] AllowedServiceTools { get; init; } = [];

    /// <summary>Gets whether activation is refused unless the request carries a passing evaluation.</summary>
    public bool RequireEvaluationBeforeActivation { get; init; }

    /// <summary>Gets the timeout in seconds for one evaluation run.</summary>
    [Range(1, 600)]
    public int EvaluationRunTimeoutSeconds { get; init; } = 120;
}
