namespace CasCap.Models;

/// <summary>Credential-free, conditionally readable current PDF identity returned by the trusted storage adapter.</summary>
internal sealed record DocumentBlobMetadata(string BlobUri, string ETag);
