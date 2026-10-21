namespace CasCap.Tests.Unit;

/// <summary>Tests process-local runtime state isolation between tenant scopes.</summary>
[Trait("Category", "Tenant Isolation")]
public sealed class TenantStateIsolationTests
{
    [Fact]
    public async Task SessionAndOverrides_AreTenantQualified()
    {
        var state = new AgentRuntimeState();
        var tenantAContext = new StaticTenantContext("tenant-a");
        var tenantBContext = new StaticTenantContext("tenant-b");
        var tenantASessions = new InMemoryAgentSessionStore(state, tenantAContext);
        var tenantBSessions = new InMemoryAgentSessionStore(state, tenantBContext);
        var tenantAOverrides = new InMemoryAgentOverrideStore(state, tenantAContext);
        var tenantBOverrides = new InMemoryAgentOverrideStore(state, tenantBContext);

        await tenantASessions.SetAsync("assistant", "v1", "shared-session", "tenant-a-state", CancellationToken.None);
        await tenantAOverrides.SetAsync(
            "assistant",
            "v1",
            "shared-session",
            new AgentOverrideState { ModelName = "tenant-a-model" },
            CancellationToken.None);

        Assert.Equal(
            "tenant-a-state",
            await tenantASessions.GetAsync("assistant", "v1", "shared-session", CancellationToken.None));
        Assert.Null(await tenantBSessions.GetAsync("assistant", "v1", "shared-session", CancellationToken.None));
        Assert.Equal(
            "tenant-a-model",
            (await tenantAOverrides.GetAsync("assistant", "v1", "shared-session", CancellationToken.None)).ModelName);
        Assert.Null(
            (await tenantBOverrides.GetAsync("assistant", "v1", "shared-session", CancellationToken.None)).ModelName);
    }

    [Fact]
    public async Task SessionKeys_DelimiterLikeIdentifiersDoNotCollide()
    {
        var state = new AgentRuntimeState();
        var firstStore = new InMemoryAgentSessionStore(state, new StaticTenantContext("tenant:alpha"));
        var secondStore = new InMemoryAgentSessionStore(state, new StaticTenantContext("tenant"));

        await firstStore.SetAsync("agent", "v1", "session", "first", CancellationToken.None);
        await secondStore.SetAsync("alpha:agent", "v1", "session", "second", CancellationToken.None);

        Assert.Equal("first", await firstStore.GetAsync("agent", "v1", "session", CancellationToken.None));
        Assert.Equal("second", await secondStore.GetAsync("alpha:agent", "v1", "session", CancellationToken.None));
    }

    [Fact]
    public async Task RedisState_SurvivesStoreReplacementAndInvalidatesByDefinitionVersion()
    {
        var distributedCache = new InMemoryDistributedCache();
        var runtimeConfig = Options.Create(new AgentRuntimeConfig());
        var tenantContext = new StaticTenantContext("tenant-a");
        var firstSessions = new RedisAgentSessionStore(distributedCache, runtimeConfig, tenantContext);
        var firstOverrides = new RedisAgentOverrideStore(distributedCache, runtimeConfig, tenantContext);

        await firstSessions.SetAsync("assistant", "v1", "session", "persisted", CancellationToken.None);
        await firstOverrides.SetAsync(
            "assistant",
            "v1",
            "session",
            new AgentOverrideState { ModelName = "model-v1" },
            CancellationToken.None);

        var replacementSessions = new RedisAgentSessionStore(distributedCache, runtimeConfig, tenantContext);
        var replacementOverrides = new RedisAgentOverrideStore(distributedCache, runtimeConfig, tenantContext);

        Assert.Equal(
            "persisted",
            await replacementSessions.GetAsync("assistant", "v1", "session", CancellationToken.None));
        Assert.Equal(
            "model-v1",
            (await replacementOverrides.GetAsync("assistant", "v1", "session", CancellationToken.None)).ModelName);
        Assert.Null(await replacementSessions.GetAsync("assistant", "v2", "session", CancellationToken.None));
        Assert.Null(
            (await replacementOverrides.GetAsync("assistant", "v2", "session", CancellationToken.None)).ModelName);
    }

    private sealed class StaticTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }
}
