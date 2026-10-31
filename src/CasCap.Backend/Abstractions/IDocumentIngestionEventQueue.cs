namespace CasCap.Abstractions;

/// <summary>Leased notification delivery; acknowledgement is separate from durable application admission.</summary>
internal interface IDocumentIngestionEventQueue
{
    /// <summary>Receives at most one message, or null for an empty queue.</summary>
    public Task<IngestionQueueMessage?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>Renews visibility and returns the replacement receipt.</summary>
    public Task<IngestionQueueMessage> RenewAsync(IngestionQueueMessage message, CancellationToken cancellationToken);

    /// <summary>Acknowledges a delivery only after its database work has committed.</summary>
    public Task AcknowledgeAsync(IngestionQueueMessage message, CancellationToken cancellationToken);

    /// <summary>Releases a message with bounded retry delay.</summary>
    public Task ReleaseAsync(IngestionQueueMessage message, CancellationToken cancellationToken);

    /// <summary>Copies the original notification into private poison storage before acknowledging it.</summary>
    public Task QuarantineAsync(IngestionQueueMessage message, CancellationToken cancellationToken);
}
