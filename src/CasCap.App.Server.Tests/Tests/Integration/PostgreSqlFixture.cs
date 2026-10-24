using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CasCap.Tests.Integration;

/// <summary>Creates an isolated test database on the Compose tests-profile PostgreSQL service.</summary>
/// <remarks>Start postgres-tests first. Only the generated database is removed; development data is never used.</remarks>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"agentizr_test_{Guid.NewGuid():N}";

    /// <summary>Gets the connection string of the isolated migrated database.</summary>
    internal string ConnectionString => new NpgsqlConnectionStringBuilder(PersistenceTestSettings.PostgreSql) { Database = _databaseName }.ConnectionString;

    /// <summary>Gets a factory for contexts connected to the migrated database.</summary>
    internal IDbContextFactory<AgentRuntimeDbContext> DbContextFactory { get; private set; } = null!;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AgentRuntimeDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        DbContextFactory = new PooledDbContextFactory<AgentRuntimeDbContext>(options);
        await using var dbContext = await DbContextFactory.CreateDbContextAsync();
        await dbContext.Database.MigrateAsync();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync();
        await dbContext.Database.EnsureDeletedAsync();
    }
}
