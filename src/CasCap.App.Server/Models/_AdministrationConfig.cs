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
