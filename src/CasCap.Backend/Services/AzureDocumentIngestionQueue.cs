using Azure.Storage.Queues;

namespace CasCap.Services;

/// <summary>Adapts Azure Queue leases without destructive dequeue or runtime provisioning.</summary>
internal sealed class AzureDocumentIngestionQueue(
    IOptions<DocumentIngestionConfig> config,
    RuntimeEntraCredential runtimeEntraCredential) : IDocumentIngestionEventQueue
{
    private readonly Lazy<QueueClient> _queue = new(() => CreateClient(config.Value.QueueUri, config, runtimeEntraCredential));
    private readonly Lazy<QueueClient> _poison = new(() => CreateClient(config.Value.PoisonQueueUri, config, runtimeEntraCredential));

    /// <inheritdoc/>
    public async Task<IngestionQueueMessage?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var response = await _queue.Value.ReceiveMessageAsync(
            TimeSpan.FromSeconds(config.Value.VisibilitySeconds), cancellationToken);
        return response.Value is { } message ? new IngestionQueueMessage
        {
            Id = message.MessageId,
            LeaseToken = message.PopReceipt,
            Body = message.Body,
        } : null;
    }

    /// <inheritdoc/>
    public async Task<IngestionQueueMessage> RenewAsync(IngestionQueueMessage message, CancellationToken cancellationToken)
    {
        var response = await _queue.Value.UpdateMessageAsync(message.Id, message.LeaseToken, message.Body,
            TimeSpan.FromSeconds(config.Value.VisibilitySeconds), cancellationToken);
        return message with { LeaseToken = response.Value.PopReceipt };
    }

    /// <inheritdoc/>
    public async Task AcknowledgeAsync(IngestionQueueMessage message, CancellationToken cancellationToken) =>
        await _queue.Value.DeleteMessageAsync(message.Id, message.LeaseToken, cancellationToken);

    /// <inheritdoc/>
    public async Task ReleaseAsync(IngestionQueueMessage message, CancellationToken cancellationToken) =>
        await _queue.Value.UpdateMessageAsync(message.Id, message.LeaseToken, message.Body,
            TimeSpan.FromMilliseconds(config.Value.PollIntervalMs), cancellationToken);

    /// <inheritdoc/>
    public async Task QuarantineAsync(IngestionQueueMessage message, CancellationToken cancellationToken)
    {
        await _poison.Value.SendMessageAsync(message.Body, cancellationToken: cancellationToken);
        await AcknowledgeAsync(message, cancellationToken);
    }

    private static QueueClient CreateClient(
        string endpoint,
        IOptions<DocumentIngestionConfig> config,
        RuntimeEntraCredential runtimeEntraCredential) =>
        new(new Uri(endpoint),
            runtimeEntraCredential.Credential
                ?? throw new InvalidOperationException("Ingestion requires an explicitly configured Azure identity."),
            new QueueClientOptions
            {
                MessageEncoding = config.Value.QueueBase64Encoded ? QueueMessageEncoding.Base64 : QueueMessageEncoding.None,
            });
}
