using CasCap.Constants;

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
        while (!ownershipToken.IsCancellationRequested)
        {
            ownershipToken.ThrowIfCancellationRequested();
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var received = await scope.ServiceProvider.GetRequiredService<DocumentIngestionAdmissionService>()
                .ReceiveAsync(ownershipToken);
            ownershipToken.ThrowIfCancellationRequested();
            var processed = config.Value.ProcessingEnabled
                && await scope.ServiceProvider.GetRequiredService<DocumentIngestionProcessingService>()
                    .ProcessOneAsync(ownershipToken);
            if (!received && !processed)
                await Task.Delay(TimeSpan.FromMilliseconds(config.Value.PollIntervalMs), timeProvider, ownershipToken);
        }
        ownershipToken.ThrowIfCancellationRequested();
    }
}
