namespace CasCap.Extensions;

/// <summary>Executes finite backend operations through the application's service provider.</summary>
public static class ServiceProviderExtensions
{
    /// <summary>Applies pending migrations in an owned scope without starting runtime services.</summary>
    /// <param name="services">Application service provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The migration operation.</returns>
    public static async Task MigrateAgentRuntimeDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<AgentRuntimeDatabaseMigrator>()
            .MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
