using Microsoft.Extensions.Logging.Abstractions;

namespace CasCap.Tests.Integration;

/// <summary>
/// Verifies the leased realtime call record against real PostgreSQL: lease renewal, a tenant-scoped end request, a single
/// claimant for an expired lease, an end reached through a non-owning registry and the sweeper's hang-up.
/// </summary>
/// <remarks>Each test uses its own call identifier, so the tests share one migrated database without interfering.</remarks>
[Trait("Category", "PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgresRealtimeCallStoreTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task RenewAsync_ExtendsTheLeaseAndStoresUsageUntilAnEndIsRequested()
    {
        var store = new PostgresRealtimeCallStore(fixture.DbContextFactory);
        var record = CreateRecord("owner-a", DateTimeOffset.UtcNow.AddSeconds(20));
        await store.AddAsync(record, TestContext.Current.CancellationToken);

        var renewed = await store.RenewAsync(record.CallId, "owner-a", DateTimeOffset.UtcNow.AddSeconds(40),
            new RealtimeCallUsage { TotalTokens = 123 }, TestContext.Current.CancellationToken);
        var foreign = await store.RequestEndAsync("tenant-b", "tutor", record.CallId, TestContext.Current.CancellationToken);
        var requested = await store.RequestEndAsync("tenant-a", "tutor", record.CallId, TestContext.Current.CancellationToken);
        var renewedAfterEnd = await store.RenewAsync(record.CallId, "owner-a", DateTimeOffset.UtcNow.AddSeconds(60),
            new RealtimeCallUsage(), TestContext.Current.CancellationToken);

        Assert.True(renewed);
        Assert.Null(foreign);
        Assert.Equal(RealtimeCallStatus.Ending, requested!.Status);
        Assert.Equal(123, requested.Usage!.TotalTokens);
        Assert.False(renewedAfterEnd);
    }

    [Fact]
    public async Task ClaimExpiredAsync_ConcurrentClaimantsTakeAnExpiredLeaseOnce()
    {
        var store = new PostgresRealtimeCallStore(fixture.DbContextFactory);
        var record = CreateRecord("lost-owner", DateTimeOffset.UtcNow.AddSeconds(-1));
        await store.AddAsync(record, TestContext.Current.CancellationToken);
        var now = DateTimeOffset.UtcNow;

        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            new PostgresRealtimeCallStore(fixture.DbContextFactory)
                .ClaimExpiredAsync($"claimant-{index}", now, now.AddSeconds(20), TestContext.Current.CancellationToken)));

        Assert.Single(claims.SelectMany(claim => claim), claimed => claimed.CallId == record.CallId);
    }

    [Fact]
    public async Task EndAsync_OnANonOwningRegistryReturnsTheOwnersSummary()
    {
        var store = new PostgresRealtimeCallStore(fixture.DbContextFactory);
        var record = CreateRecord("other-replica", DateTimeOffset.UtcNow.AddSeconds(20));
        await store.AddAsync(record, TestContext.Current.CancellationToken);
        var registry = CreateRegistry(store, new ServiceCollection().BuildServiceProvider());
        var summary = new RealtimeCallSummaryResponse
        {
            EndReason = RealtimeCallEndReason.ClientRequested,
            DurationSeconds = 42,
            Usage = new RealtimeCallUsage { TotalTokens = 1000 },
        };

        var ending = registry.EndAsync("tenant-a", "tutor", record.CallId, TestContext.Current.CancellationToken);
        // The owning replica sees the end request on its next renewal, hangs up and records the summary.
        while ((await store.GetAsync("tenant-a", "tutor", record.CallId, TestContext.Current.CancellationToken))!.Status
            != RealtimeCallStatus.Ending)
            await Task.Delay(50, TestContext.Current.CancellationToken);
        await store.CompleteAsync(record.CallId, summary, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Equal(summary, await ending);
    }

    [Fact]
    public async Task MaintainAsync_SweepsAnExpiredCallAndDeletesItAfterRetention()
    {
        var hangUps = new List<string>();
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new AgentRuntimeConfig
            {
                Tenants =
                {
                    ["tenant-a"] = new()
                    {
                        Providers =
                        {
                            ["realtime"] = new ProviderConfig { Type = AgentType.AzureOpenAI, ModelName = "gpt-realtime", ApiKey = "example-key" },
                        },
                    },
                },
            }))
            .AddSingleton(new RuntimeEntraCredential(() => null))
            .AddSingleton(new AzureRealtimeClient(new HttpClient(new RecordingHandler(hangUps))))
            .BuildServiceProvider();
        var store = new PostgresRealtimeCallStore(fixture.DbContextFactory);
        var record = CreateRecord("lost-replica", DateTimeOffset.UtcNow.AddSeconds(-1));
        await store.AddAsync(record, TestContext.Current.CancellationToken);

        await CreateRegistry(store, services).MaintainAsync(TestContext.Current.CancellationToken);
        var swept = await store.GetAsync("tenant-a", "tutor", record.CallId, TestContext.Current.CancellationToken);
        await store.DeleteEndedBeforeAsync(DateTimeOffset.UtcNow.AddMinutes(1), TestContext.Current.CancellationToken);

        Assert.Equal(RealtimeCallStatus.Ended, swept!.Status);
        Assert.Equal(RealtimeCallEndReason.ControlLost, swept.Summary!.EndReason);
        Assert.Contains(hangUps, path => path.Contains(record.ProviderCallId, StringComparison.Ordinal));
        Assert.Null(await store.GetAsync("tenant-a", "tutor", record.CallId, TestContext.Current.CancellationToken));
    }

    private static RealtimeCallRegistry CreateRegistry(IRealtimeCallStore store, IServiceProvider services) => new(
        Options.Create(new AgentRuntimeConfig()),
        store,
        services.GetRequiredService<IServiceScopeFactory>(),
        TimeProvider.System,
        NullLogger<RealtimeCallRegistry>.Instance);

    private static RealtimeCallRecord CreateRecord(string ownerId, DateTimeOffset leaseExpiresAt) => new()
    {
        CallId = Guid.NewGuid().ToString("N"),
        TenantId = "tenant-a",
        AgentName = "tutor",
        OwnerId = ownerId,
        Status = RealtimeCallStatus.Active,
        ProviderName = "realtime",
        Endpoint = new Uri("https://example.openai.azure.com/"),
        ProviderCallId = $"rtc_{Guid.NewGuid():N}",
        StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        LeaseExpiresAt = leaseExpiresAt,
    };

    private sealed class RecordingHandler(List<string> paths) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (paths)
                paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
