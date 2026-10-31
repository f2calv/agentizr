#if DEBUG
using System.Text.Json;

namespace CasCap.Tests;

/// <summary>Credential-free source notifications for ingestion tests.</summary>
internal static class DocumentIngestionTestData
{
    /// <summary>Gets a synthetic trusted container mapping and isolated election resource.</summary>
    public static DocumentIngestionConfig Config => new()
    {
        LeadershipResource = "example:isolated:ingestion",
        QueueUri = "https://queue.example.com/events",
        PoisonQueueUri = "https://queue.example.com/poison",
        Sources =
        [
            new()
            {
                Topic = "/example/storage",
                ContainerUri = "https://storage.example.com/documents",
                TenantId = "tenant-a",
                CollectionId = "manuals",
            },
        ],
    };

    /// <summary>Builds an Event Grid notification with no document contents or real Azure identifiers.</summary>
    public static BinaryData Body(
        string id = "event-1",
        string sequencer = "1",
        bool deleted = false,
        string topic = "/example/storage",
        string url = "https://storage.example.com/documents/manual.pdf") =>
        BinaryData.FromObjectAsJson(new
        {
            id,
            topic,
            subject = "/blobServices/default/containers/documents/blobs/manual.pdf",
            eventType = deleted ? "Microsoft.Storage.BlobDeleted" : "Microsoft.Storage.BlobCreated",
            eventTime = "2026-10-25T00:00:00Z",
            dataVersion = "1",
            metadataVersion = "1",
            data = new { url, sequencer, eTag = "example-etag" },
        });

    /// <summary>Builds a normalized, application-bound storage event.</summary>
    public static DocumentIngestionNotification Notification(
        string id = "event-1",
        string sequencer = "1",
        bool deleted = false,
        string tenant = "tenant-a") => new()
        {
            Event = BlobStorageEventReader.Read(Body(id, sequencer, deleted)),
            TenantId = tenant,
            CollectionId = "manuals",
        };
}
#endif
