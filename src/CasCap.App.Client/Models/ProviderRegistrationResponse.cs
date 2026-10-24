namespace CasCap.Client.Models;

/// <summary>Describes one configured provider registration without credentials or raw configuration.</summary>
/// <remarks>
/// Registrations are the server's current configuration. Published definitions embed their own copy of
/// the provider settings, so changing a registration never rewrites an existing version.
/// </remarks>
public sealed record ProviderRegistrationResponse
{
    /// <summary>Gets the stable provider name that definitions reference.</summary>
    /// <example>azure-openai</example>
    public required string Name { get; init; }

    /// <summary>Gets the provider type.</summary>
    /// <example>AzureOpenAI</example>
    public required string Type { get; init; }

    /// <summary>Gets the scheme, host and port of the endpoint, without path, query or user information.</summary>
    /// <example>https://example.com</example>
    public string? EndpointAuthority { get; init; }

    /// <summary>Gets the configured model.</summary>
    /// <example>gpt-5-mini</example>
    public required string ModelName { get; init; }

    /// <summary>Gets the configured reasoning effort.</summary>
    /// <example>Low</example>
    public string? ReasoningEffort { get; init; }

    /// <summary>Gets how the runtime authenticates: <c>ApiKey</c>, <c>EntraId</c> or <c>None</c>.</summary>
    /// <example>EntraId</example>
    public required string CredentialSource { get; init; }

    /// <summary>Gets whether the definition policy permits this registration's endpoint authority.</summary>
    /// <example>true</example>
    public bool IsEndpointAllowed { get; init; }
}
