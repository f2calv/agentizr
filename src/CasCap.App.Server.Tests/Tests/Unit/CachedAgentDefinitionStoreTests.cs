using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CasCap.Tests.Unit;

/// <summary>Tests definition cache population and invalidation around the PostgreSQL authority.</summary>
[Trait("Category", "Agent Runtime")]
public sealed class CachedAgentDefinitionStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly InMemoryDistributedCache _cache = new();
    private TestDbContextFactory _dbContextFactory = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AgentRuntimeDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContextFactory = new TestDbContextFactory(options);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task ActivateAsync_SelectsVersionQualifiedCachedDefinition()
    {
        var tenantContext = new StaticTenantContext("tenant-a");
        var innerStore = new PostgresAgentDefinitionStore(_dbContextFactory, tenantContext, TimeProvider.System);
        var store = new CachedAgentDefinitionStore(
            innerStore,
            _cache,
            Options.Create(new AgentRuntimeConfig()),
            NullLogger<CachedAgentDefinitionStore>.Instance,
            tenantContext);
        await innerStore.PublishAsync(CreateDefinition("v1"), 1, null, null, CancellationToken.None);
        await innerStore.ActivateAsync("assistant", "v1", null, null, CancellationToken.None);

        var first = await store.GetAsync("assistant", CancellationToken.None);
        var version1Key = AgentDefinitionCacheKey.Create("tenant-a", "assistant", "v1");
        var cached = await _cache.Get<AgentDefinition>(version1Key);
        await store.PublishAsync(CreateDefinition("v2"), 1, null, null, CancellationToken.None);
        var afterPublish = await _cache.Get<AgentDefinition>(version1Key);
        await store.ActivateAsync("assistant", "v2", null, null, CancellationToken.None);
        var second = await store.GetAsync("assistant", CancellationToken.None);
        var version2Key = AgentDefinitionCacheKey.Create("tenant-a", "assistant", "v2");
        var cachedVersion2 = await _cache.Get<AgentDefinition>(version2Key);

        Assert.Equal("v1", first?.Version);
        Assert.Equal("v1", cached?.Version);
        Assert.Null(cached?.Provider.ApiKey);
        Assert.Equal("v1", afterPublish?.Version);
        Assert.Equal("v2", second?.Version);
        Assert.Equal("v2", cachedVersion2?.Version);
    }

    private static AgentDefinition CreateDefinition(string version) => new()
    {
        Name = "assistant",
        Version = version,
        Agent = new AgentConfig
        {
            Provider = "provider",
            Name = "Assistant",
            Description = "Test assistant",
            Prompt = "Test prompt",
        },
        Provider = new ProviderConfig
        {
            Type = AgentType.Ollama,
            ModelName = "test-model",
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
}
