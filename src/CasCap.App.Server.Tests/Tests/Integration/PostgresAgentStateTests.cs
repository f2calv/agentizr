using CasCap.Common.Services;
using CasCap.Exceptions;
using CasCap.IntegrationTests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StackExchange.Redis;

namespace CasCap.Tests.Integration;

/// <summary>Checks durable state, revision-qualified caching and session coordination across independent hosts.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PostgreSQL")]
[Trait("Category", "Redis")]
[Collection(HostIntegrationCollection.Name)]
public sealed class PostgresAgentStateTests(PostgreSqlFixture postgres, RedisStateFixture redis)
    : IClassFixture<PostgreSqlFixture>, IClassFixture<RedisStateFixture>
{
    [Fact]
    public async Task State_SurvivesCacheEvictionAndReplicaReplacement()
    {
        var tenant = new StaticTenantContext(Guid.NewGuid().ToString("N"));
        var first = CreateStore(tenant);
        var sessions = new PostgresAgentSessionStore(first, tenant);
        var overrides = new PostgresAgentOverrideStore(first, tenant);
        await using (await first.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken))
        {
            await sessions.SetAsync("assistant", "v1", "session", "persisted", TestContext.Current.CancellationToken);
            await sessions.SetSnapshotAsync("assistant", "v1", "session", "saved", "snapshot", TestContext.Current.CancellationToken);
            await overrides.SetAsync("assistant", "v1", "session",
                new AgentOverrideState { ModelName = "model-v1" }, TestContext.Current.CancellationToken);
        }
        Assert.Equal("persisted", await sessions.GetAsync("assistant", "v1", "session", TestContext.Current.CancellationToken));
        await using var db = await postgres.DbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var state = await db.AgentStates.SingleAsync(record => record.TenantId == tenant.TenantId
            && record.Key == AgentStateKey.CreateRedis("session", tenant.TenantId, "assistant", "v1", "session"),
            TestContext.Current.CancellationToken);
        await redis.Connection.GetDatabase().KeyDeleteAsync($"{state.Key}:{state.Revision:N}");

        var replacement = CreateStore(tenant);
        var replacementSessions = new PostgresAgentSessionStore(replacement, tenant);
        Assert.Equal("persisted", await replacementSessions.GetAsync("assistant", "v1", "session", TestContext.Current.CancellationToken));
        Assert.Equal("snapshot", await replacementSessions.GetSnapshotAsync("assistant", "v1", "session", "saved", TestContext.Current.CancellationToken));
        Assert.Equal("model-v1", (await new PostgresAgentOverrideStore(replacement, tenant)
            .GetAsync("assistant", "v1", "session", TestContext.Current.CancellationToken)).ModelName);
        Assert.Null(await replacementSessions.GetAsync("assistant", "v2", "session", TestContext.Current.CancellationToken));
        var otherTenant = new StaticTenantContext("other-" + tenant.TenantId);
        Assert.Null(await new PostgresAgentSessionStore(CreateStore(otherTenant), otherTenant)
            .GetAsync("assistant", "v1", "session", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task State_ObsoleteCachedRevisionCannotOverwriteNewPayloadOrResurrectDeletion()
    {
        var tenant = new StaticTenantContext(Guid.NewGuid().ToString("N"));
        var store = CreateStore(tenant);
        var sessions = new PostgresAgentSessionStore(store, tenant);
        await using (await store.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken))
            await sessions.SetAsync("assistant", "v1", "session", "old", TestContext.Current.CancellationToken);
        Assert.Equal("old", await sessions.GetAsync("assistant", "v1", "session", TestContext.Current.CancellationToken));
        await using (await store.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken))
            await sessions.SetAsync("assistant", "v1", "session", "new", TestContext.Current.CancellationToken);
        Assert.Equal("new", await sessions.GetAsync("assistant", "v1", "session", TestContext.Current.CancellationToken));
        await using (await store.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken))
            await sessions.DeleteAsync("assistant", "v1", "session", TestContext.Current.CancellationToken);
        Assert.Null(await sessions.GetAsync("assistant", "v1", "session", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task State_WorksWithoutRedisAndRequiresSessionOwnership()
    {
        var tenant = new StaticTenantContext(Guid.NewGuid().ToString("N"));
        var store = CreateStore(tenant, cacheEnabled: false);
        var sessions = new PostgresAgentSessionStore(store, tenant);
        await Assert.ThrowsAsync<AgentSessionConflictException>(async () =>
            await sessions.SetAsync("assistant", "v1", "session", "unowned", TestContext.Current.CancellationToken));
        await using (await store.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken))
            await sessions.SetAsync("assistant", "v1", "session", "durable", TestContext.Current.CancellationToken);
        Assert.Equal("durable", await sessions.GetAsync("assistant", "v1", "session", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AcquireAsync_ExcludesOtherReplicaAndReleasesOwnership()
    {
        var tenant = new StaticTenantContext(Guid.NewGuid().ToString("N"));
        var first = CreateStore(tenant);
        var second = CreateStore(tenant);
        var lease = await first.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<AgentSessionConflictException>(async () =>
            await second.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken));
        await lease.DisposeAsync();
        await using var next = await second.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken);
        await lease.DisposeAsync();
        await Assert.ThrowsAsync<AgentSessionConflictException>(async () =>
            await first.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteAsync_LostDatabaseConnectionCannotWriteAfterAnotherOwnerAcquires()
    {
        var tenant = new StaticTenantContext(Guid.NewGuid().ToString("N"));
        var first = CreateStore(tenant);
        var second = CreateStore(tenant);
        await using var lost = await first.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken);
        await using var db = await postgres.DbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            SELECT pg_terminate_backend(pid) FROM pg_locks
            WHERE locktype = 'advisory' AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
            """, TestContext.Current.CancellationToken);
        await using var next = await second.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken);
        var newSessions = new PostgresAgentSessionStore(second, tenant);
        await newSessions.SetAsync("assistant", "v1", "session", "new-owner", TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<NpgsqlException>(async () =>
            await new PostgresAgentSessionStore(first, tenant).SetAsync(
                "assistant", "v1", "session", "stale", TestContext.Current.CancellationToken));
        Assert.Equal("new-owner", await newSessions.GetAsync("assistant", "v1", "session", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoHosts_RunExcludesResetAndAnotherRunUntilStateIsCommitted()
    {
        var executor = new BlockingExecutor();
        using var root = new AgentizrWebApplicationFactory();
        using var firstHost = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            AddDurableState(services);
            services.RemoveAll<IAgentExecutor>();
            services.AddSingleton<IAgentExecutor>(executor);
        }));
        using var secondHost = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(AddDurableState));
        using var firstClient = firstHost.CreateClient();
        using var secondClient = secondHost.CreateClient();
        var sessionId = Guid.NewGuid().ToString("N");
        var request = new RunAgentRequest { SessionId = sessionId, Input = "hello" };
        var run = firstClient.PostAsJsonAsync("/api/v1/agents/assistant/runs", request, TestContext.Current.CancellationToken);
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        try
        {
            using var reset = await secondClient.DeleteAsync($"/api/v1/agents/assistant/sessions/{sessionId}", TestContext.Current.CancellationToken);
            using var overlap = await secondClient.PostAsJsonAsync("/api/v1/agents/assistant/runs", request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Conflict, reset.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        }
        finally
        {
            executor.Release.TrySetResult();
        }
        using var completed = await run;
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var client = new AgentRuntimeClient(secondClient);
        Assert.True((await client.GetSessionAsync("assistant", sessionId, TestContext.Current.CancellationToken))?.Exists);
        Assert.True(await client.ResetSessionAsync("assistant", sessionId, TestContext.Current.CancellationToken));
        Assert.False((await client.GetSessionAsync("assistant", sessionId, TestContext.Current.CancellationToken))?.Exists);
    }

    [Fact]
    public async Task State_UnavailableRedisFallsBackToPostgreSql()
    {
        await using var unavailable = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { { "localhost", 1 } },
            AbortOnConnectFail = false,
            ConnectRetry = 0,
            ConnectTimeout = 100,
            AsyncTimeout = 100,
            BacklogPolicy = BacklogPolicy.FailFast,
        });
        var tenant = new StaticTenantContext(Guid.NewGuid().ToString("N"));
        var cachingConfig = Options.Create(new CachingConfig());
        var store = new PostgresAgentStateStore(NullLogger<PostgresAgentStateStore>.Instance,
            Options.Create(new AgentRuntimeConfig()), cachingConfig, TimeProvider.System, postgres.DbContextFactory, tenant,
            new RedisCacheService(NullLogger<RedisCacheService>.Instance, cachingConfig, unavailable, TimeProvider.System));
        var sessions = new PostgresAgentSessionStore(store, tenant);
        await using (await store.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken))
            await sessions.SetAsync("assistant", "v1", "session", "durable", TestContext.Current.CancellationToken);
        Assert.Equal("durable", await sessions.GetAsync("assistant", "v1", "session", TestContext.Current.CancellationToken));
    }

    private void AddDurableState(IServiceCollection services)
    {
        services.AddScoped<PostgresAgentStateStore>(provider => CreateStore(provider.GetRequiredService<ITenantContext>()));
        services.RemoveAll<IAgentSessionLock>();
        services.AddScoped<IAgentSessionLock>(provider => provider.GetRequiredService<PostgresAgentStateStore>());
        services.RemoveAll<IAgentSessionStore>();
        services.AddScoped<IAgentSessionStore, PostgresAgentSessionStore>();
        services.RemoveAll<IAgentOverrideStore>();
        services.AddScoped<IAgentOverrideStore, PostgresAgentOverrideStore>();
    }

    private PostgresAgentStateStore CreateStore(ITenantContext tenant, bool cacheEnabled = true)
    {
        var cachingConfig = Options.Create(new CachingConfig());
        var remoteCache = cacheEnabled
            ? new RedisCacheService(NullLogger<RedisCacheService>.Instance, cachingConfig, redis.Connection, TimeProvider.System)
            : null;
        return new PostgresAgentStateStore(NullLogger<PostgresAgentStateStore>.Instance,
            Options.Create(new AgentRuntimeConfig()), cachingConfig, TimeProvider.System, postgres.DbContextFactory, tenant, remoteCache);
    }

    private sealed class StaticTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }

    private sealed class BlockingExecutor : IAgentExecutor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<AgentExecutionResult> ExecuteAsync(AgentExecutionContext context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new AgentExecutionResult { OutputText = "done", SessionStateJson = "committed" };
        }
    }
}
