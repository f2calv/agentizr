using Azure;
using Azure.Storage.Blobs;
using System.Runtime.CompilerServices;

namespace CasCap.Services;

/// <summary>Uses the configured runtime identity to page through originals and verify individual source revisions.</summary>
internal sealed class AzureDocumentBlobInventory(
    IOptions<DocumentIngestionConfig> config,
    RuntimeEntraCredential runtimeEntraCredential) : IDocumentBlobInventory
{
    /// <inheritdoc/>
    public async IAsyncEnumerable<DocumentBlobMetadata> ListAsync(
        DocumentSourceConfig source, [EnumeratorCancellation] CancellationToken token)
    {
        var container = Container(source.ContainerUri);
        await foreach (var page in container.GetBlobsAsync(cancellationToken: token).AsPages(pageSizeHint: 100))
            foreach (var item in page.Values)
                if (item.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && item.Properties.ETag is { } etag)
                    yield return new(container.GetBlobClient(item.Name).Uri.AbsoluteUri, etag.ToString());
    }

    /// <inheritdoc/>
    public async Task<DocumentBlobMetadata?> ReadAsync(string blobUri, CancellationToken token)
    {
        var uri = new Uri(blobUri);
        var source = config.Value.Sources.SingleOrDefault(item =>
            uri.AbsoluteUri.StartsWith(item.ContainerUri.TrimEnd('/') + "/", StringComparison.Ordinal));
        if (source is null || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.UserInfo.Length != 0 || uri.AbsoluteUri != blobUri)
            throw new ArgumentException("The document is not a canonical configured source.", nameof(blobUri));
        var name = Uri.UnescapeDataString(blobUri[(source.ContainerUri.TrimEnd('/').Length + 1)..]);
        if (name.Replace('\\', '/').Split('/').Any(segment => segment is "." or ".."))
            throw new ArgumentException("Relative document names are unsupported.", nameof(blobUri));
        var blob = Container(source.ContainerUri).GetBlobClient(name);
        try
        {
            var properties = await blob.GetPropertiesAsync(cancellationToken: token);
            return new(blob.Uri.AbsoluteUri, properties.Value.ETag.ToString());
        }
        catch (RequestFailedException exception) when (exception.Status == 404 && exception.ErrorCode == "BlobNotFound")
        {
            return null;
        }
    }

    private BlobContainerClient Container(string uri) => new(new Uri(uri),
        runtimeEntraCredential.Credential
            ?? throw new InvalidOperationException("Document inventory requires an explicitly configured Azure identity."));
}
