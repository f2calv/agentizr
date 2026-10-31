using Azure;
using Azure.Storage.Blobs;

namespace CasCap.Services;

/// <summary>Revalidates the configured source binding and opens the admitted revision through the shared storage library.</summary>
internal sealed class DocumentBlobDownloadService
{
    private readonly IOptions<DocumentIngestionConfig> _config;
    private readonly Func<Uri, IAzBlobStorageBase> _storageFactory;

    /// <summary>Uses the existing runtime identity without creating containers or opening connections at construction.</summary>
    public DocumentBlobDownloadService(
        IOptions<DocumentIngestionConfig> config,
        RuntimeEntraCredential runtimeEntraCredential
    ) : this(config, container => new DocumentStorage(new BlobContainerClient(container,
        runtimeEntraCredential.Credential
            ?? throw new InvalidOperationException("Document downloads require an explicitly configured Azure identity."))))
    {
    }

    internal DocumentBlobDownloadService(
        IOptions<DocumentIngestionConfig> config,
        Func<Uri, IAzBlobStorageBase> storageFactory
    )
    {
        _config = config;
        _storageFactory = storageFactory;
    }

    /// <summary>Opens a caller-owned stream; Azure 404/412 and transient failures propagate, including during reads.</summary>
    public Task<Stream> OpenReadAsync(DocumentIngestionClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(claim.ETag);
        if (!Uri.TryCreate(claim.BlobUri, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.Equals(claim.BlobUri, uri.AbsoluteUri, StringComparison.Ordinal))
            throw new ArgumentException("A canonical credential-free HTTPS document location is required.", nameof(claim));
        var source = _config.Value.Sources.SingleOrDefault(candidate =>
            string.Equals(candidate.TenantId, claim.TenantId, StringComparison.Ordinal)
            && string.Equals(candidate.CollectionId, claim.CollectionId, StringComparison.Ordinal)
            && uri.AbsoluteUri.StartsWith(candidate.ContainerUri.TrimEnd('/') + "/", StringComparison.Ordinal));
        if (source is null || !uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The PDF source is not assigned to the claimed application collection.", nameof(claim));
        var blobName = Uri.UnescapeDataString(uri.AbsoluteUri[(source.ContainerUri.TrimEnd('/').Length + 1)..]);
        if (blobName.Replace('\\', '/').Split('/').Any(segment => segment is "." or ".."))
            throw new ArgumentException("Relative path segments are not supported in document names.", nameof(claim));
        return _storageFactory(new Uri(source.ContainerUri))
            .OpenBlobReadAsync(blobName, new ETag(claim.ETag), cancellationToken);
    }

    private sealed class DocumentStorage(BlobContainerClient containerClient) : AzBlobStorageBase(containerClient);
}
