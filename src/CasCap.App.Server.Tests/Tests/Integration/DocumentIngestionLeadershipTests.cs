#if DEBUG
using CasCap.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CasCap.Tests.Integration;

/// <summary>Verifies the consumer's ownership boundary with a fake runner, not Redis election itself.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "Ingestion")]
public sealed class DocumentIngestionLeadershipTests
{
    [Fact]
    public async Task AdmissionStartsOnlyInsideOwnershipAndStopsOnLoss()
    {
        var queue = new BlockingQueue();
        var runner = new CapturingRunner();
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentIngestionEventQueue>(queue);
        services.AddSingleton<IDocumentIngestionInbox, UnusedInbox>();
        services.AddSingleton(Options.Create(DocumentIngestionTestData.Config));
        services.AddSingleton<ILogger<DocumentIngestionAdmissionService>>(NullLogger<DocumentIngestionAdmissionService>.Instance);
        services.AddScoped<DocumentIngestionAdmissionService>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var feature = new DocumentIngestionFeature(Options.Create(DocumentIngestionTestData.Config),
            TimeProvider.System, runner, provider.GetRequiredService<IServiceScopeFactory>());
        using var host = new CancellationTokenSource();

        var run = feature.ExecuteAsync(host.Token);
        Assert.Equal(0, queue.Receives);
        Assert.Equal(DocumentIngestionTestData.Config.LeadershipResource, runner.Resource);
        using var ownership = new CancellationTokenSource();
        var owned = runner.Callback!(ownership.Token);
        await queue.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, queue.Receives);
        await ownership.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owned);
        Assert.False(host.IsCancellationRequested);
        await host.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessingIsNotResolvedBeforeOwnershipOrWhenDisabled(bool enabled)
    {
        var config = Options.Create(DocumentIngestionTestData.Config with { ProcessingEnabled = enabled });
        var queue = new CancellingQueue { CancelOnReceive = !enabled };
        var runner = new CapturingRunner();
        var resolutions = 0;
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentIngestionEventQueue>(queue);
        services.AddSingleton<IDocumentIngestionInbox, UnusedInbox>();
        services.AddSingleton(config);
        services.AddSingleton<ILogger<DocumentIngestionAdmissionService>>(NullLogger<DocumentIngestionAdmissionService>.Instance);
        services.AddScoped<DocumentIngestionAdmissionService>();
        services.AddScoped(_ =>
        {
            resolutions++;
            return new DocumentIngestionProcessingService(NullLogger<DocumentIngestionProcessingService>.Instance,
                config, TimeProvider.System, new DocumentIngestionJobService(config, TimeProvider.System, new UnusedFactory()), []);
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var feature = new DocumentIngestionFeature(config,
            TimeProvider.System, runner, provider.GetRequiredService<IServiceScopeFactory>());
        using var host = new CancellationTokenSource();
        var run = feature.ExecuteAsync(host.Token);
        Assert.Equal(0, queue.Receives);
        Assert.Equal(0, resolutions);
        using var ownership = new CancellationTokenSource();
        queue.Owner = ownership;
        if (enabled)
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.Callback!(ownership.Token));
            Assert.Contains("real registered document processor", exception.Message, StringComparison.Ordinal);
        }
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.Callback!(ownership.Token));
        Assert.Equal(1, queue.Receives);
        Assert.Equal(enabled ? 1 : 0, resolutions);
        await host.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    private sealed class CapturingRunner : ILeadershipRunner
    {
        public Func<CancellationToken, Task>? Callback { get; private set; }
        public string? Resource { get; private set; }
        public Task RunAsync(string resource, Func<CancellationToken, Task> runAsync, CancellationToken cancellationToken)
        {
            Resource = resource;
            Callback = runAsync;
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class BlockingQueue : IDocumentIngestionEventQueue
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Receives { get; private set; }
        public async Task<IngestionQueueMessage?> ReceiveAsync(CancellationToken cancellationToken)
        {
            Receives++;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
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

    private sealed class CancellingQueue : IDocumentIngestionEventQueue
    {
        public bool CancelOnReceive { get; init; }
        public CancellationTokenSource Owner { get; set; } = null!;
        public int Receives { get; private set; }
        public async Task<IngestionQueueMessage?> ReceiveAsync(CancellationToken cancellationToken)
        {
            Receives++;
            if (CancelOnReceive)
                await Owner.CancelAsync();
            return null;
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

    private sealed class UnusedFactory : IDbContextFactory<AgentRuntimeDbContext>
    {
        public AgentRuntimeDbContext CreateDbContext() => throw new NotSupportedException();
    }

    private sealed class UnusedInbox : IDocumentIngestionInbox
    {
        public Task AcceptAsync(DocumentIngestionNotification notification, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
#endif
