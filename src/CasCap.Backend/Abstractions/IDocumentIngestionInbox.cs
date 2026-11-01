namespace CasCap.Abstractions;

/// <summary>Commits idempotent notification receipt and tenant-owned document state atomically.</summary>
internal interface IDocumentIngestionInbox
{
    /// <summary>Accepts a validated event; completion means a durable commit, including duplicate receipt.</summary>
    public Task AcceptAsync(DocumentIngestionNotification notification, CancellationToken cancellationToken);
}
