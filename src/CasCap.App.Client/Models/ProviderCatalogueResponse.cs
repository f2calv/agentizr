namespace CasCap.Client.Models;

/// <summary>Returns an app's provider registrations and the policy applied to published definitions.</summary>
public sealed record ProviderCatalogueResponse
{
    /// <summary>Gets the configured provider registrations, ordered by name.</summary>
    public required IReadOnlyList<ProviderRegistrationResponse> Providers { get; init; }

    /// <summary>Gets the provider type names definitions may use.</summary>
    /// <example>["AzureOpenAI", "Ollama", "OpenAI"]</example>
    public required IReadOnlyList<string> SupportedProviderTypes { get; init; }

    /// <summary>Gets the reasoning effort names a provider may request.</summary>
    /// <example>["Low", "Medium", "High"]</example>
    public required IReadOnlyList<string> ReasoningEfforts { get; init; }

    /// <summary>Gets the provider endpoint authorities definitions may use.</summary>
    /// <example>["https://example.com"]</example>
    public required IReadOnlyList<string> AllowedProviderAuthorities { get; init; }

    /// <summary>Gets the remote MCP endpoint authorities definitions may use.</summary>
    /// <example>["https://mcp.example.com"]</example>
    public required IReadOnlyList<string> AllowedMcpAuthorities { get; init; }

    /// <summary>Gets the in-process service tool names definitions may use.</summary>
    /// <example>["DateTimeTools"]</example>
    public required IReadOnlyList<string> AllowedServiceTools { get; init; }

    /// <summary>Gets the definition schema versions the server accepts.</summary>
    /// <example>[1]</example>
    public required IReadOnlyList<int> SupportedSchemaVersions { get; init; }

    /// <summary>Gets whether activation requires a passing fixture evaluation.</summary>
    /// <example>false</example>
    public bool EvaluationRequiredForActivation { get; init; }

    /// <summary>Gets whether the server can publish and activate definitions.</summary>
    /// <remarks>Configuration-backed development hosts are read-only.</remarks>
    /// <example>true</example>
    public bool PublicationSupported { get; init; }
}
