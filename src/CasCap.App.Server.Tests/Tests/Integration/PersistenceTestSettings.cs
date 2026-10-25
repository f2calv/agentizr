using CasCap.Common.Extensions;
using Microsoft.Extensions.Configuration;

namespace CasCap.Tests.Integration;

/// <summary>Loads public localhost-only test connections from the test project's configuration.</summary>
internal static class PersistenceTestSettings
{
    /// <summary>Gets the local server connection used to create an isolated PostgreSQL database.</summary>
    public static string PostgreSql => GetConnectionString(nameof(PostgreSql));

    /// <summary>Gets the local server connection used with per-run Redis keys.</summary>
    public static string Redis => GetConnectionString(nameof(Redis));

    private static string GetConnectionString(string name)
    {
        using var configuration = new ConfigurationManager();
        configuration.AddStandardConfiguration("Test", typeof(PersistenceTestSettings).Assembly);
        return configuration.GetConnectionString(name)
            ?? throw new InvalidOperationException($"The {name} test connection is required.");
    }
}
