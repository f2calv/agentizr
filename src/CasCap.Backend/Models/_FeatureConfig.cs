using CasCap.Constants;

namespace CasCap.Models;

/// <summary>Selects immutable startup roles from the common application image.</summary>
public sealed record FeatureConfig : IAppConfig
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(FeatureConfig)}";

    /// <summary>Gets comma-separated roles; existing deployments default to the runtime host.</summary>
    [Required, MinLength(1)]
    public string EnabledFeatures { get; init; } = FeatureNames.Agentizr;

    /// <summary>Validates role names and rejects combining the finite migrator with daemons.</summary>
    public HashSet<string> GetEnabledFeatures()
    {
        var names = EnabledFeatures.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowed = new HashSet<string>([FeatureNames.Agentizr, FeatureNames.Migrate, FeatureNames.Ingestion],
            StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0 || names.Any(name => !allowed.Contains(name)))
            throw new InvalidOperationException("Select agentizr, ingestion, agentizr,ingestion or migrate.");
        if (names.Contains(FeatureNames.Migrate) && names.Count != 1)
            throw new InvalidOperationException("The finite migrate role cannot be combined with daemon roles.");
        return names;
    }
}
