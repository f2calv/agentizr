using CasCap.Constants;

namespace CasCap;

public static partial class AppHost
{
    private const string MigrateArgument = "--migrate";

    private static (string[] ConfigurationArgs, bool MigrateArgument) ParseMigrationMode(string[] args)
    {
        var migrateArgument = args.Contains(MigrateArgument, StringComparer.Ordinal);
        var configurationArgs = args.Where(argument => argument != MigrateArgument).ToArray();
        return (configurationArgs, migrateArgument);
    }

    private static FeatureConfig ApplyMigrationOverride(
        FeatureConfig configured,
        bool migrateArgument) =>
        migrateArgument
            ? configured with { EnabledFeatures = FeatureNames.Migrate }
            : configured;

    private static async Task RunDatabaseMigrationAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<AgentRuntimeDatabaseMigrator>()
            .MigrateAsync(cancellationToken);
    }
}
