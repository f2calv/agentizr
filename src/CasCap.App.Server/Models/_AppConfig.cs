namespace CasCap.Models;

/// <summary>Application-wide telemetry configuration.</summary>
public sealed record AppConfig : IAppConfig, IMetricsConfig
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => nameof(AppConfig);

    /// <inheritdoc/>
    [Required, MinLength(1)]
    public string MetricNamePrefix { get; init; } = "agentizr";

    /// <inheritdoc/>
    [Required, MinLength(1)]
    public string OtelServiceName { get; init; } = "agentizr";

    /// <inheritdoc/>
    public Uri? OtlpExporterEndpoint { get; init; }
}
