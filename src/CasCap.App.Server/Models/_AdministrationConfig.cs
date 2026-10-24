namespace CasCap.Models;

/// <summary>Human administration settings: who may manage which app through the browser frontend.</summary>
public sealed record AdministrationConfig : IAppConfig, IValidatableObject
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(AdministrationConfig)}";

    /// <summary>Gets whether authenticated human administration is enabled.</summary>
    /// <remarks>
    /// When disabled, administration is open only in Development with tenant authentication also disabled,
    /// and refused everywhere else.
    /// </remarks>
    public bool Enabled { get; init; }

    /// <summary>Gets the OpenID Connect issuer authority of the dedicated administration web registration.</summary>
    [Url]
    public string? Authority { get; init; }

    /// <summary>Gets the client identifier of the dedicated administration web registration.</summary>
    public string? ClientId { get; init; }

    /// <summary>Gets the client secret of the administration web registration, from the secret configuration chain.</summary>
    public string? ClientSecret { get; init; }

    /// <summary>Gets the local sign-in callback path registered as an exact HTTPS redirect URI.</summary>
    [Required, MinLength(2)]
    public string CallbackPath { get; init; } = "/signin-oidc";

    /// <summary>Gets the local sign-out callback path registered as an exact HTTPS redirect URI.</summary>
    [Required, MinLength(2)]
    public string SignedOutCallbackPath { get; init; } = "/signout-callback-oidc";

    /// <summary>Gets the sliding lifetime of the administration session cookie, in minutes.</summary>
    [Range(5, 24 * 60)]
    public int SessionLifetimeMinutes { get; init; } = 8 * 60;

    /// <summary>Gets whether identity provider metadata retrieval requires HTTPS.</summary>
    public bool HttpsMetadataRequired { get; init; } = true;

    /// <summary>Gets the validated claim containing the stable administrator user identifier.</summary>
    [Required, MinLength(1)]
    public string AdministratorClaimType { get; init; } = "oid";

    /// <summary>Gets administrator user identifiers keyed by the app each may manage.</summary>
    /// <remarks>An assignment grants read, publish and activation rights on that app only.</remarks>
    public Dictionary<string, string[]> AppAdministrators { get; init; } = [];

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enabled)
            yield break;
        if (string.IsNullOrWhiteSpace(Authority) || string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
            yield return new ValidationResult(
                $"{nameof(Authority)}, {nameof(ClientId)} and {nameof(ClientSecret)} are required when administration is enabled.",
                [nameof(Authority), nameof(ClientId), nameof(ClientSecret)]);
        if (AppAdministrators.Count == 0)
            yield return new ValidationResult(
                $"{nameof(AppAdministrators)} must assign at least one administrator when administration is enabled.",
                [nameof(AppAdministrators)]);
        if (AppAdministrators.Any(entry => string.IsNullOrWhiteSpace(entry.Key)
            || entry.Value.Length == 0
            || entry.Value.Any(string.IsNullOrWhiteSpace)))
            yield return new ValidationResult(
                $"{nameof(AppAdministrators)} entries need a non-empty app and non-empty administrator identifiers.",
                [nameof(AppAdministrators)]);
    }
}
