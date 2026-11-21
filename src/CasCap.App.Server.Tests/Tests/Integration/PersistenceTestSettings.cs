using CasCap.Common.Extensions;
using Microsoft.Extensions.Configuration;

namespace CasCap.Tests.Integration;

/// <summary>Loads public localhost-only test connections from the test project's configuration.</summary>
internal sealed class PersistenceTestSettings
{
    private const string ConfigurationSectionName = "ConnectionStrings";

    /// <summary>Gets the local server connection used to create an isolated PostgreSQL database.</summary>
    public static string PostgreSql => RequireConnection(Load().PostgreSqlConnection, nameof(PostgreSql));

    /// <summary>Gets the local server connection used with per-run Redis keys.</summary>
    public static string Redis => RequireConnection(Load().RedisConnection, nameof(Redis));

    [ConfigurationKeyName(nameof(PostgreSql))]
    public string? PostgreSqlConnection { get; init; }

    [ConfigurationKeyName(nameof(Redis))]
    public string? RedisConnection { get; init; }

    private static PersistenceTestSettings Load()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddStandardConfiguration("Test", typeof(PersistenceTestSettings).Assembly);
        return configuration.GetSection(ConfigurationSectionName).Get<PersistenceTestSettings>() ?? new();
    }

    private static string RequireConnection(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Configure ConnectionStrings:{name} for the disposable test server.");
}
