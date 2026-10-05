namespace CasCap.Models;

/// <summary>JWT authentication settings for resolving trusted tenant identity.</summary>
public sealed record TenantAuthenticationConfig : IAppConfig
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(TenantAuthenticationConfig)}";

    /// <summary>Gets whether JWT tenant authentication is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the trusted token issuer authority.</summary>
    [Url]
    public string? Authority { get; init; }

    /// <summary>Gets the required Agent Runtime audience.</summary>
    public string? Audience { get; init; }

    /// <summary>Gets the validated claim containing the stable tenant identifier.</summary>
    [Required, MinLength(1)]
    public string TenantClaimType { get; init; } = "tenant_id";

    /// <summary>Gets whether metadata retrieval requires HTTPS.</summary>
    public bool HttpsMetadataRequired { get; init; } = true;
}
