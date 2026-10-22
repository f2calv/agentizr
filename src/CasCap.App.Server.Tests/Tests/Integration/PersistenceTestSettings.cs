using Microsoft.Extensions.Configuration;

namespace CasCap.Tests.Integration;

/// <summary>Loads public localhost-only test connections from the test project's configuration.</summary>
internal static class PersistenceTestSettings
{
    private static readonly IConfigurationRoot _configuration = new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Test.json"), optional: false)
        .Build();

    /// <summary>Gets the isolated Compose test server's PostgreSQL connection.</summary>
    public static string PostgreSql => _configuration.GetConnectionString(nameof(PostgreSql))
        ?? throw new InvalidOperationException("The PostgreSql test connection is required.");

    /// <summary>Gets the isolated Compose test server's Redis connection.</summary>
    public static string Redis => _configuration.GetConnectionString(nameof(Redis))
        ?? throw new InvalidOperationException("The Redis test connection is required.");
}
