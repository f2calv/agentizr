namespace CasCap.Models;

/// <summary>PostgreSQL settings for the Agent Runtime definition authority.</summary>
public sealed record DatabaseConfig : IAppConfig
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(DatabaseConfig)}";

    /// <summary>Gets the PostgreSQL connection string supplied by a private configuration provider.</summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Gets the database that replaces the one named in <see cref="ConnectionString"/>, so applications
    /// sharing one server connection string each use their own database.
    /// </summary>
    /// <remarks>Defaults to <see langword="null"/>, which keeps the connection string's database.</remarks>
    public string? DatabaseName { get; init; }
}
