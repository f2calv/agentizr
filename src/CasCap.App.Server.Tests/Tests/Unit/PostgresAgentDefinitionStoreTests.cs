using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CasCap.Tests.Unit;

/// <summary>Tests immutable definition publication and active-version history semantics.</summary>
[Trait("Category", "Agent Runtime")]
public sealed class PostgresAgentDefinitionStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AdjustableTimeProvider _timeProvider = new(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero));
    private TestDbContextFactory _dbContextFactory = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AgentRuntimeDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContextFactory = new TestDbContextFactory(options);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task DefinitionLifecycle_PublishesActivatesAndRetainsHistory()
    {
        var store = new PostgresAgentDefinitionStore(
            _dbContextFactory,
            new StaticTenantContext("tenant-a"),
            _timeProvider);

        await store.PublishAsync(CreateDefinition("v1", "model-v1"), 1, "operator", "initial", CancellationToken.None);
        Assert.Null(await store.GetAsync("assistant", CancellationToken.None));
        Assert.True(await store.ActivateAsync("assistant", "v1", "operator", "initial activation", CancellationToken.None));
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await store.PublishAsync(CreateDefinition("v2", "model-v2"), 1, "operator", "upgrade", CancellationToken.None);
        Assert.Equal("v1", (await store.GetAsync("assistant", CancellationToken.None))?.Version);
        Assert.True(await store.ActivateAsync("assistant", "v2", "operator", "upgrade activation", CancellationToken.None));

        var active = await store.GetAsync("assistant", CancellationToken.None);
        var history = await store.GetHistoryAsync("assistant", 10, CancellationToken.None);
        var activations = await store.GetActivationHistoryAsync("assistant", 10, CancellationToken.None);

        Assert.NotNull(active);
        Assert.Equal("v2", active.Version);
        Assert.Equal("model-v2", active.Provider.ModelName);
        Assert.Null(active.Provider.ApiKey);
        Assert.Equal(["v2", "v1"], history.Select(item => item.DefinitionVersion));
        Assert.All(history, item => Assert.Equal(1, item.SchemaVersion));
        Assert.Equal(["upgrade", "initial"], history.Select(item => item.ChangeReason));
        Assert.True(history[0].IsActive);
        Assert.False(history[1].IsActive);
        Assert.Equal(["v2", "v1"], activations.Select(item => item.DefinitionVersion));
        Assert.Equal(["upgrade activation", "initial activation"], activations.Select(item => item.ChangeReason));

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var persistedJson = await dbContext.AgentDefinitionSnapshots
            .OrderBy(snapshot => snapshot.Id)
            .Select(snapshot => snapshot.DefinitionJson)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.All(persistedJson, json => Assert.DoesNotContain("must-not-persist", json, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishAsync_DuplicateVersionIsRejected()
    {
        var store = new PostgresAgentDefinitionStore(
            _dbContextFactory,
            new StaticTenantContext("tenant-a"),
            _timeProvider);
        var definition = CreateDefinition("v1", "model-v1");
        await store.PublishAsync(definition, 1, null, null, CancellationToken.None);
        await store.ActivateAsync("assistant", "v1", null, null, CancellationToken.None);

        await Assert.ThrowsAsync<CasCap.Exceptions.AgentDefinitionVersionConflictException>(async () =>
            await store.PublishAsync(definition, 1, null, null, CancellationToken.None));

        var active = await store.GetAsync("assistant", CancellationToken.None);
        var history = await store.GetHistoryAsync("assistant", 10, CancellationToken.None);
        Assert.Equal("v1", active?.Version);
        Assert.Single(history);
    }

    [Fact]
    public async Task Definitions_AreTenantIsolated()
    {
        var tenantA = new PostgresAgentDefinitionStore(
            _dbContextFactory,
            new StaticTenantContext("tenant-a"),
            _timeProvider);
        var tenantB = new PostgresAgentDefinitionStore(
            _dbContextFactory,
            new StaticTenantContext("tenant-b"),
            _timeProvider);

        await tenantA.PublishAsync(CreateDefinition("v1", "model-a"), 1, null, null, CancellationToken.None);
        await tenantA.ActivateAsync("assistant", "v1", null, null, CancellationToken.None);

        Assert.Null(await tenantB.GetAsync("assistant", CancellationToken.None));
        await tenantB.PublishAsync(CreateDefinition("v1", "model-b"), 1, null, null, CancellationToken.None);
        await tenantB.ActivateAsync("assistant", "v1", null, null, CancellationToken.None);
        Assert.Equal("model-a", (await tenantA.GetAsync("assistant", CancellationToken.None))?.Provider.ModelName);
        Assert.Equal("model-b", (await tenantB.GetAsync("assistant", CancellationToken.None))?.Provider.ModelName);
    }

    private static AgentDefinition CreateDefinition(string version, string modelName) => new()
    {
        Name = "assistant",
        Version = version,
        Agent = new AgentConfig
        {
            Provider = "provider",
            Name = "assistant",
            Description = "Test assistant",
            Prompt = "Test prompt",
        },
        Provider = new ProviderConfig
        {
            Type = AgentType.Ollama,
            ModelName = modelName,
            Endpoint = new Uri("http://localhost:11434"),
            ApiKey = "must-not-persist",
        },
    };

    private sealed class TestDbContextFactory(
        DbContextOptions<AgentRuntimeDbContext> options) : IDbContextFactory<AgentRuntimeDbContext>
    {
        public AgentRuntimeDbContext CreateDbContext() => new(options);

        public Task<AgentRuntimeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class StaticTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
