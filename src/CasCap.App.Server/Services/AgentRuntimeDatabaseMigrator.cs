using Microsoft.EntityFrameworkCore;

namespace CasCap.Services;

/// <summary>Applies pending Agent Runtime migrations for an external deployment job.</summary>
internal sealed class AgentRuntimeDatabaseMigrator(
    ILogger<AgentRuntimeDatabaseMigrator> logger,
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory)
{
    /// <summary>Applies pending migrations and returns when the schema is current.</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("{ClassName} applying Agent Runtime database migrations", nameof(AgentRuntimeDatabaseMigrator));
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("{ClassName} Agent Runtime database migrations are current", nameof(AgentRuntimeDatabaseMigrator));
    }
}