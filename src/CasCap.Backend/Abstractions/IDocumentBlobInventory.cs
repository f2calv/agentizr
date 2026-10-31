namespace CasCap.Abstractions;

/// <summary>Reads configured original-PDF inventory without creating, changing or deleting storage objects.</summary>
internal interface IDocumentBlobInventory
{
    /// <summary>Streams one complete configured container inventory; enumeration failures propagate.</summary>
    public IAsyncEnumerable<DocumentBlobMetadata> ListAsync(DocumentSourceConfig source, CancellationToken token);

    /// <summary>Reads current metadata; null means BlobNotFound, never container, permission or transport failure.</summary>
    public Task<DocumentBlobMetadata?> ReadAsync(string blobUri, CancellationToken token);
}
