using StackExchange.Redis;

namespace CasCap.Tests.Integration;

/// <summary>Connects to the Compose tests-profile Redis service, never the development or deployed cache.</summary>
public sealed class RedisStateFixture : IAsyncLifetime
{
    /// <summary>Gets the connection shared by independent simulated runtime replicas.</summary>
    internal IConnectionMultiplexer Connection { get; private set; } = null!;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        Connection = await ConnectionMultiplexer.ConnectAsync(PersistenceTestSettings.Redis);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Connection is not null)
            await Connection.DisposeAsync();
    }
}
