using CasCap.Constants;
using CasCap.Common.Services;
using Cronos;

namespace CasCap.Services;

/// <summary>Runs serial admission and gated processing only inside the shared ingestion leadership lifetime.</summary>
internal sealed class DocumentIngestionFeature(
    IOptions<DocumentIngestionConfig> config,
    TimeProvider timeProvider,
    ILeadershipRunner leadershipRunner,
    IServiceScopeFactory serviceScopeFactory) : IBgFeature
{
    /// <inheritdoc/>
    public string FeatureName => FeatureNames.Ingestion;

    /// <inheritdoc/>
    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        leadershipRunner.RunAsync(config.Value.LeadershipResource, RunOwnedAsync, cancellationToken);

    private async Task RunOwnedAsync(CancellationToken ownershipToken)
    {
        if (!config.Value.SynchronizationEnabled)
        {
            await RunAdmissionAsync(ownershipToken);
            return;
        }
        await RequestSynchronizationAsync(ownershipToken);
        Func<CancellationToken, Task>[] loops =
        [
            RunAdmissionAsync,
            token => CoalescingScheduleRunner.RunAsync(CronExpression.Parse(config.Value.SynchronizationCron),
                timeProvider, (_, scheduleToken) => RequestSynchronizationAsync(scheduleToken), token),
        ];
        await Parallel.ForEachAsync(loops, new ParallelOptions
        {
            CancellationToken = ownershipToken,
            MaxDegreeOfParallelism = loops.Length,
        }, async (loop, token) => await loop(token));
    }

    private async Task RequestSynchronizationAsync(CancellationToken token)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DocumentSynchronizationService>().RequestAsync(null, token);
    }

    private async Task RunAdmissionAsync(CancellationToken ownershipToken)
    {
        while (!ownershipToken.IsCancellationRequested)
        {
            ownershipToken.ThrowIfCancellationRequested();
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var synchronized = config.Value.SynchronizationEnabled
                && await scope.ServiceProvider.GetRequiredService<DocumentSynchronizationService>().ProcessNextAsync(ownershipToken);
            var received = await scope.ServiceProvider.GetRequiredService<DocumentIngestionAdmissionService>()
                .ReceiveAsync(ownershipToken);
            ownershipToken.ThrowIfCancellationRequested();
            var processed = config.Value.ProcessingEnabled
                && await scope.ServiceProvider.GetRequiredService<DocumentIngestionProcessingService>()
                    .ProcessOneAsync(ownershipToken);
            ownershipToken.ThrowIfCancellationRequested();
            var indexed = scope.ServiceProvider.GetRequiredService<IOptions<DocumentIndexingConfig>>().Value.ProcessingEnabled
                && await scope.ServiceProvider.GetRequiredService<DocumentIndexingProcessingService>().ProcessOneAsync(ownershipToken);
            if (!received && !processed && !synchronized && !indexed)
                await Task.Delay(TimeSpan.FromMilliseconds(config.Value.PollIntervalMs), timeProvider, ownershipToken);
        }
        ownershipToken.ThrowIfCancellationRequested();
    }
}
