#if DEBUG
using CasCap.Common.Abstractions;
using CasCap.Constants;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RedLockNet;
using StackExchange.Redis;

namespace CasCap.Tests.Integration;

/// <summary>Starts real ingestion-only hosts against isolated PostgreSQL and shared Redis, with no Azure deliveries.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PostgreSQL")]
[Trait("Category", "Redis")]
[Trait("Category", "PreparationProviders")]
public sealed class DocumentIngestionHostIntegrationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _database;

    public DocumentIngestionHostIntegrationTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task IngestionOnlyStartupAndGracefulHandover()
    {
        var token = TestContext.Current.CancellationToken;
        var resource = $"example:test:ingestion:{Guid.NewGuid():N}";
        var firstQueue = new WaitingQueue();
        var secondQueue = new WaitingQueue();
        await using var first = new Factory(_database.ConnectionString, resource, firstQueue);
        await using var second = new Factory(_database.ConnectionString, resource, secondQueue);
        using var firstClient = first.CreateClient();
        await firstQueue.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
        using var secondClient = second.CreateClient();
        await AssertHostAsync(first, firstClient, token);
        await AssertHostAsync(second, secondClient, token);
        await using (var competing = await second.Services.GetRequiredService<IDistributedLockFactory>()
            .CreateLockAsync(resource, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100), token))
            Assert.False(competing.IsAcquired);
        Assert.False(secondQueue.Entered.Task.IsCompleted);
        await first.DisposeAsync();
        await firstQueue.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
        await secondQueue.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
        Assert.True(firstQueue.OwnershipToken.IsCancellationRequested);
        Assert.False(secondQueue.OwnershipToken.IsCancellationRequested);
        Assert.Equal(HttpStatusCode.OK, (await secondClient.GetAsync("/healthz", token)).StatusCode);
        await second.DisposeAsync();
        await secondQueue.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
        Assert.True(secondQueue.OwnershipToken.IsCancellationRequested);
        await using var db = _database.DbContextFactory.CreateDbContext();
        Assert.Empty(db.DocumentIngestionJobs);
        Assert.Empty(db.DocumentPreparationOperations);
    }

    private static async Task AssertHostAsync(Factory factory, HttpClient client, CancellationToken token)
    {
        var status = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/status", token);
        Assert.Equal(FeatureNames.Ingestion, status.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz", token)).StatusCode);
        Assert.Equal(["/healthz", "/status"], factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText).Order().ToArray());
        Assert.False(factory.Services.GetRequiredService<IOptions<DocumentIngestionConfig>>().Value.ProcessingEnabled);
        Assert.NotNull(factory.Services.GetRequiredService<ILeadershipRunner>());
        using var scope = factory.Services.CreateScope();
        Assert.IsType<DocumentPreparationProcessor>(Assert.Single(scope.ServiceProvider.GetServices<IDocumentProcessor>()));
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<HybridDocumentProcessor>());
        Assert.IsType<DocumentPreparationProvider>(scope.ServiceProvider.GetRequiredService<IDocumentPreparationProvider>());
    }

    private sealed class Factory(string connection, string resource, WaitingQueue queue) : WebApplicationFactory<Program>
    {
        private IConnectionMultiplexer? _connection;
        private IDisposable? _locks;

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Startup reads roles before Build, so deferred web-host configuration alone is too late.
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(Settings()));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(Settings()));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDocumentIngestionEventQueue>();
                services.AddSingleton<IDocumentIngestionEventQueue>(queue);
                _connection = services.Single(service => service.ServiceType == typeof(IConnectionMultiplexer))
                    .ImplementationInstance as IConnectionMultiplexer
                    ?? throw new InvalidOperationException("The real Redis registration is missing.");
                _locks = services.Single(service => service.ServiceType == typeof(IDistributedLockFactory))
                    .ImplementationInstance as IDisposable
                    ?? throw new InvalidOperationException("The real distributed-lock registration is missing.");
            });
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await base.DisposeAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _locks, null)?.Dispose();
                Interlocked.Exchange(ref _connection, null)?.Dispose();
            }
        }

        private Dictionary<string, string?> Settings() => new()
        {
            [$"{FeatureConfig.ConfigurationSectionName}:EnabledFeatures"] = FeatureNames.Ingestion,
            [$"{DatabaseConfig.ConfigurationSectionName}:ConnectionString"] = connection,
            [$"{DatabaseConfig.ConfigurationSectionName}:DatabaseName"] = string.Empty,
            [$"{CachingConfig.ConfigurationSectionName}:RemoteCache:IsEnabled"] = "true",
            [$"{CachingConfig.ConfigurationSectionName}:RemoteCache:SerializationType"] = nameof(SerializationType.Json),
            [$"{CachingConfig.ConfigurationSectionName}:RemoteCacheConnectionString"] = PersistenceTestSettings.Redis,
            [$"{CachingConfig.ConfigurationSectionName}:DistributedLockingEnabled"] = "true",
            [$"{CachingConfig.ConfigurationSectionName}:CacheExpiryServiceEnabled"] = "false",
            [$"{CachingConfig.ConfigurationSectionName}:Redlock:RedisKeyFormat"] = "RedLock:{0}",
            [$"{CachingConfig.ConfigurationSectionName}:Redlock:Profiles:LeaderElection:ExpiryMs"] = "10000",
            [$"{CachingConfig.ConfigurationSectionName}:Redlock:Profiles:LeaderElection:WaitMs"] = "1000",
            [$"{CachingConfig.ConfigurationSectionName}:Redlock:Profiles:LeaderElection:RetryMs"] = "100",
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:LeadershipResource"] = resource,
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:QueueUri"] = DocumentIngestionTestData.Config.QueueUri,
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:PoisonQueueUri"] = DocumentIngestionTestData.Config.PoisonQueueUri,
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:ProcessingEnabled"] = "false",
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:Sources:0:Topic"] = DocumentIngestionTestData.Config.Sources[0].Topic,
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:Sources:0:ContainerUri"] = DocumentIngestionTestData.Config.Sources[0].ContainerUri,
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:Sources:0:TenantId"] = DocumentIngestionTestData.Config.Sources[0].TenantId,
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:Sources:0:CollectionId"] = DocumentIngestionTestData.Config.Sources[0].CollectionId,
            ["AppConfig:KeyVaultName"] = "skip",
            ["AppConfig:OtlpExporterEndpoint"] = string.Empty,
        };
    }

    private sealed class WaitingQueue : IDocumentIngestionEventQueue
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken OwnershipToken { get; private set; }

        public async Task<IngestionQueueMessage?> ReceiveAsync(CancellationToken cancellationToken)
        {
            OwnershipToken = cancellationToken;
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The empty test queue unexpectedly returned.");
            }
            finally
            {
                Stopped.TrySetResult();
            }
        }

        public Task<IngestionQueueMessage> RenewAsync(IngestionQueueMessage message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AcknowledgeAsync(IngestionQueueMessage message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ReleaseAsync(IngestionQueueMessage message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task QuarantineAsync(IngestionQueueMessage message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
#endif
