namespace CasCap.Models;

/// <summary>PostgreSQL settings for the Agent Runtime definition authority.</summary>
public sealed record AgentRuntimeDatabaseConfig : IAppConfig
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(AgentRuntimeDatabaseConfig)}";

    /// <summary>Gets the PostgreSQL connection string supplied by a private configuration provider.</summary>
    public string? ConnectionString { get; init; }
}
