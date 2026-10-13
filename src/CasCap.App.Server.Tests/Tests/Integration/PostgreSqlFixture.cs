using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Testcontainers.PostgreSql;

namespace CasCap.Tests.Integration;

/// <summary>Starts one disposable PostgreSQL server and applies the Agent Runtime migrations to it.</summary>
/// <remarks>Requires a running Docker engine.</remarks>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    /// <summary>Gets a factory for contexts connected to the migrated database.</summary>
    internal IDbContextFactory<AgentRuntimeDbContext> DbContextFactory { get; private set; } = null!;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var options = new DbContextOptionsBuilder<AgentRuntimeDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        DbContextFactory = new PooledDbContextFactory<AgentRuntimeDbContext>(options);
        await using var dbContext = await DbContextFactory.CreateDbContextAsync();
        await dbContext.Database.MigrateAsync();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
