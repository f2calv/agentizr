#if DEBUG
using Npgsql;

namespace CasCap.Tests.Integration;

/// <summary>Owns an empty isolated database for logging tests, without unrelated application/vector migrations.</summary>
public sealed class RuntimeLoggingPostgreSqlFixture : IAsyncLifetime
{
    private readonly string _database = $"agentizr_logging_test_{Guid.NewGuid():N}";
    private bool _created;

    /// <summary>Gets the isolated authority connection; individual tests apply the real configuration migrations.</summary>
    public string ConnectionString =>
        new NpgsqlConnectionStringBuilder(PersistenceTestSettings.PostgreSql) { Database = _database, Pooling = false }.ConnectionString;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(PersistenceTestSettings.PostgreSql);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        _created = true;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (!_created)
            return;
        await using var connection = new NpgsqlConnection(PersistenceTestSettings.PostgreSql);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{_database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
#endif
