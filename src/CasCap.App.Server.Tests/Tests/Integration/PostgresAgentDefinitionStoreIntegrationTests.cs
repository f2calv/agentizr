using CasCap.Exceptions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>
/// Verifies the definition store against real PostgreSQL: migrations, <c>jsonb</c> storage, unique-version
/// conflicts and the advisory lock that serialises concurrent activations.
/// </summary>
/// <remarks>Each test uses its own tenant, so the tests share one migrated database without interfering.</remarks>
[Trait("Category", "PostgreSQL")]
public sealed class PostgresAgentDefinitionStoreIntegrationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task Migrations_AreAppliedAndStoreDefinitionsAsJsonb()
    {
        await using var dbContext = await fixture.DbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);

        var pending = await dbContext.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);
        var columnType = await dbContext.Database
            .SqlQuery<string>($"SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_schema = 'agent_runtime' AND column_name = 'definition_json'")
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Empty(pending);
        Assert.Equal("jsonb", columnType);
    }

    [Fact]
    public async Task PublishAndActivate_RoundTripsTheDefinitionWithoutTheApiKey()
    {
        var store = CreateStore();
        var definition = CreateDefinition("v1");

        await store.PublishAsync(definition, 1, "publisher", "initial", TestContext.Current.CancellationToken);
        var activated = await store.ActivateAsync("assistant", "v1", "activator", "go live", TestContext.Current.CancellationToken);
        var stored = await store.GetAsync("assistant", TestContext.Current.CancellationToken);

        Assert.True(activated);
        Assert.NotNull(stored);
        Assert.Equal(definition with { Provider = definition.Provider with { ApiKey = null } }, stored, new DefinitionComparer());
    }

    [Fact]
    public async Task PublishAsync_DuplicateVersion_Conflicts()
    {
        var store = CreateStore();
        await store.PublishAsync(CreateDefinition("v1"), 1, null, null, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AgentDefinitionVersionConflictException>(async () =>
            await store.PublishAsync(CreateDefinition("v1"), 1, null, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishAsync_ConcurrentDuplicateVersion_StoresOneSnapshot()
    {
        var store = CreateStore();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            try
            {
                await store.PublishAsync(CreateDefinition("v1"), 1, null, null, TestContext.Current.CancellationToken);
                return true;
            }
            catch (AgentDefinitionVersionConflictException)
            {
                return false;
            }
        }));
        var history = await store.GetHistoryAsync("assistant", 10, TestContext.Current.CancellationToken);

        Assert.Equal(1, results.Count(published => published));
        Assert.Single(history);
    }

    [Fact]
    public async Task ActivateAsync_ConcurrentActivations_LeaveOneActivePointerAndAFullAudit()
    {
        var store = CreateStore();
        var versions = Enumerable.Range(1, 8).Select(i => $"v{i}").ToArray();
        foreach (var version in versions)
            await store.PublishAsync(CreateDefinition(version), 1, null, null, TestContext.Current.CancellationToken);

        var activated = await Task.WhenAll(versions.Select(version =>
            store.ActivateAsync("assistant", version, "activator", null, TestContext.Current.CancellationToken).AsTask()));
        var active = await store.GetActiveAsync("assistant", TestContext.Current.CancellationToken);
        var activations = await store.GetActivationHistoryAsync("assistant", 20, TestContext.Current.CancellationToken);
        var history = await store.GetHistoryAsync("assistant", 20, TestContext.Current.CancellationToken);

        Assert.All(activated, Assert.True);
        Assert.Equal(versions.Length, activations.Count);
        // The advisory lock serialises activations, so the pointer matches the last one committed.
        Assert.Equal(activations[0].DefinitionVersion, active?.Definition.Version);
        Assert.Single(history, item => item.IsActive);
    }

    [Fact]
    public async Task Definitions_AreIsolatedBetweenTenants()
    {
        var tenantA = CreateStore();
        var tenantB = CreateStore();
        await tenantA.PublishAsync(CreateDefinition("v1"), 1, null, null, TestContext.Current.CancellationToken);
        await tenantA.ActivateAsync("assistant", "v1", null, null, TestContext.Current.CancellationToken);

        var fromB = await tenantB.GetAsync("assistant", TestContext.Current.CancellationToken);
        var activatedInB = await tenantB.ActivateAsync("assistant", "v1", null, null, TestContext.Current.CancellationToken);

        Assert.Null(fromB);
        Assert.False(activatedInB);
    }

    private PostgresAgentDefinitionStore CreateStore() =>
        new(fixture.DbContextFactory, new StaticTenantContext($"tenant-{Guid.NewGuid():N}"), TimeProvider.System);

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
            Tools = [new ToolSource { Endpoint = "http://tools.example.test/mcp", IncludeTools = ["get_status"] }],
        },
        Provider = new ProviderConfig
        {
            Type = AgentType.OpenAI,
            ModelName = "test-model",
            Endpoint = new Uri("http://models.example.test/v1"),
            ApiKey = "must-not-persist",
        },
    };

    private sealed class StaticTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }

    /// <summary>Compares definitions by their JSON form, since they contain collections without value equality.</summary>
    private sealed class DefinitionComparer : IEqualityComparer<AgentDefinition>
    {
        public bool Equals(AgentDefinition? x, AgentDefinition? y) =>
            JsonSerializer.Serialize(x, JsonSerializerOptions.Web) == JsonSerializer.Serialize(y, JsonSerializerOptions.Web);

        public int GetHashCode(AgentDefinition obj) => JsonSerializer.Serialize(obj, JsonSerializerOptions.Web).GetHashCode(StringComparison.Ordinal);
    }
}
