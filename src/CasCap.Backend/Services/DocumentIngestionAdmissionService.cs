using System.Text.Json;

namespace CasCap.Services;

/// <summary>Validates one leased notification, then commits admission before acknowledging delivery.</summary>
internal sealed partial class DocumentIngestionAdmissionService(
    ILogger<DocumentIngestionAdmissionService> logger,
    IOptions<DocumentIngestionConfig> config,
    IDocumentIngestionEventQueue documentIngestionEventQueue,
    IDocumentIngestionInbox documentIngestionInbox)
{
    /// <summary>Processes one notification; returns false only when no message was available.</summary>
    public async Task<bool> ReceiveAsync(CancellationToken cancellationToken)
    {
        var message = await documentIngestionEventQueue.ReceiveAsync(cancellationToken);
        if (message is null)
            return false;

        DocumentIngestionNotification notification;
        try
        {
            notification = Parse(message.Body);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        {
            // Provider parse exceptions can contain document content; never send them to telemetry.
            LogQuarantined(logger);
            await documentIngestionEventQueue.QuarantineAsync(message, cancellationToken);
            return true;
        }
        try
        {
            await documentIngestionInbox.AcceptAsync(notification, cancellationToken);
        }
        catch (JsonException)
        {
            // Conflicting receipt identities are invalid input; infrastructure failures must propagate.
            LogQuarantined(logger);
            await documentIngestionEventQueue.QuarantineAsync(message, cancellationToken);
            return true;
        }
        cancellationToken.ThrowIfCancellationRequested();
        await documentIngestionEventQueue.AcknowledgeAsync(message, cancellationToken);
        LogAccepted(logger);
        return true;
    }

    private DocumentIngestionNotification Parse(BinaryData body)
    {
        if (body.ToMemory().Length > 64 * 1024)
            throw new JsonException("The event exceeds the ingestion envelope limit.");
        var notification = BlobStorageEventReader.Read(body);
        var source = config.Value.Sources.SingleOrDefault(candidate =>
            string.Equals(candidate.Topic, notification.Topic, StringComparison.OrdinalIgnoreCase)
            && notification.BlobUri.AbsoluteUri.StartsWith(candidate.ContainerUri.TrimEnd('/') + "/", StringComparison.Ordinal));
        if (source is null)
            throw new JsonException("The source is not assigned to an application collection.");
        var container = new Uri(source.ContainerUri);
        var prefix = $"/blobServices/default/containers/{container.Segments[^1].TrimEnd('/')}/blobs/";
        var blobName = notification.BlobUri.AbsoluteUri[(source.ContainerUri.TrimEnd('/').Length + 1)..];
        if (!string.Equals(notification.Subject, prefix + Uri.UnescapeDataString(blobName), StringComparison.Ordinal)
            || !notification.BlobUri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new JsonException("The storage subject or file type does not match the configured source.");
        return new DocumentIngestionNotification
        {
            Event = notification,
            TenantId = source.TenantId,
            CollectionId = source.CollectionId,
            AgentPreparation = source.AgentPreparation,
        };
    }

    [LoggerMessage(LogLevel.Warning, "{ClassName} quarantined an invalid or unassigned document notification")]
    private static partial void LogQuarantined(ILogger logger, string className = nameof(DocumentIngestionAdmissionService));

    [LoggerMessage(LogLevel.Debug, "{ClassName} committed document notification admission")]
    private static partial void LogAccepted(ILogger logger, string className = nameof(DocumentIngestionAdmissionService));
}
