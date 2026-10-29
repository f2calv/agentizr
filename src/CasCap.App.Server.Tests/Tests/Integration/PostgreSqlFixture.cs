using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CasCap.Tests.Integration;

/// <summary>Creates an isolated test database on the local Compose PostgreSQL service.</summary>
/// <remarks>Start postgres first. Only the generated database is removed; development data is never used.</remarks>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"agentizr_test_{Guid.NewGuid():N}";

    /// <summary>Gets an optional baseline migration for an isolated upgrade test.</summary>
    internal string? InitialMigration { get; init; }

    /// <summary>Gets the connection string of the isolated migrated database.</summary>
    internal string ConnectionString => new NpgsqlConnectionStringBuilder(PersistenceTestSettings.PostgreSql) { Database = _databaseName }.ConnectionString;

    /// <summary>Gets a factory for contexts connected to the migrated database.</summary>
    internal IDbContextFactory<AgentRuntimeDbContext> DbContextFactory { get; private set; } = null!;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        DbContextFactory = CreateDbContextFactory();
        await using var dbContext = await DbContextFactory.CreateDbContextAsync();
        await dbContext.GetService<IMigrator>().MigrateAsync(InitialMigration);
    }

    /// <summary>Creates contexts for the same isolated database with optional transaction checkpoints.</summary>
    internal IDbContextFactory<AgentRuntimeDbContext> CreateDbContextFactory(params IInterceptor[] interceptors) =>
        new PooledDbContextFactory<AgentRuntimeDbContext>(new DbContextOptionsBuilder<AgentRuntimeDbContext>()
            .UseNpgsql(ConnectionString).AddInterceptors(interceptors).Options);

    /// <summary>Waits for an actual blocked advisory lock in this fixture's database, not an arbitrary timing delay.</summary>
    internal async Task WaitForBlockedAdvisoryLockAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await using var db = DbContextFactory.CreateDbContext();
        while (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM pg_locks locks JOIN pg_stat_activity activity ON activity.pid = locks.pid
                WHERE activity.datname = current_database() AND locks.locktype = 'advisory' AND NOT locks.granted) AS "Value"
            """).SingleAsync(deadline.Token))
            await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync();
        await dbContext.Database.EnsureDeletedAsync();
    }
}
