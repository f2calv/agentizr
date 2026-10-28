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

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync();
        await dbContext.Database.EnsureDeletedAsync();
    }
}
