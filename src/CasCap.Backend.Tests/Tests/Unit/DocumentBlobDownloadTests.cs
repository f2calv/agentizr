#if DEBUG
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace CasCap.Tests.Unit;

/// <summary>Verifies application binding, exact source identity and shared-library delegation without external access.</summary>
[Trait("Category", "BlobDownload")]
public sealed class DocumentBlobDownloadTests
{
    [Theory]
    [InlineData("manual.pdf", "manual.pdf")]
    [InlineData("nested/manual%20one.pdf", "nested/manual one.pdf")]
    [InlineData("literal%252Fmanual.pdf", "literal%2Fmanual.pdf")]
    public async Task SharedConditionalRead(string escaped, string expectedName)
    {
        var blob = new ObservedBlob();
        var service = Create(blob);
        await using (var stream = await service.OpenReadAsync(Claim() with
        {
            BlobUri = $"https://storage.example.com/documents/{escaped}",
        }, TestContext.Current.CancellationToken))
        {
            Assert.Same(blob.Content, stream);
            Assert.True(stream.CanRead);
            Assert.Equal(expectedName, blob.RequestedName);
            Assert.Equal(new ETag("example-etag"), blob.Options!.Conditions.IfMatch);
            Assert.Equal(65_536, blob.Options.BufferSize);
            Assert.Equal(TestContext.Current.CancellationToken, blob.Token);
        }
        Assert.False(blob.Content.CanRead);
    }

    [Theory]
    [InlineData("https://other.example.com/documents/manual.pdf", "tenant-a", "manuals")]
    [InlineData("https://storage.example.com/documents-other/manual.pdf", "tenant-a", "manuals")]
    [InlineData("https://storage.example.com/documents/manual.pdf", "tenant-b", "manuals")]
    [InlineData("https://storage.example.com/documents/manual.pdf", "tenant-a", "private")]
    [InlineData("https://storage.example.com/documents/manual.pdf?sig=example", "tenant-a", "manuals")]
    [InlineData("https://storage.example.com/documents/manual.pdf#fragment", "tenant-a", "manuals")]
    [InlineData("http://storage.example.com/documents/manual.pdf", "tenant-a", "manuals")]
    [InlineData("https://storage.example.com/documents/../other/manual.pdf", "tenant-a", "manuals")]
    [InlineData("https://storage.example.com/documents/nested%2F..%2F..%2Fother%2Fmanual.pdf", "tenant-a", "manuals")]
    [InlineData("https://storage.example.com/documents/..%5Cother%5Cmanual.pdf", "tenant-a", "manuals")]
    [InlineData("https://storage.example.com/documents/readme.txt", "tenant-a", "manuals")]
    public async Task UnassignedOrNoncanonicalSource(string uri, string tenant, string collection)
    {
        var service = new DocumentBlobDownloadService(Options.Create(DocumentIngestionTestData.Config),
            _ => throw new InvalidOperationException("No storage client should be requested."));
        await Assert.ThrowsAsync<ArgumentException>(() => service.OpenReadAsync(Claim() with
        {
            BlobUri = uri,
            TenantId = tenant,
            CollectionId = collection,
        }, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    public async Task RevisionIsRequired(string? tag)
    {
        var blob = new ObservedBlob();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Create(blob).OpenReadAsync(
            Claim() with { ETag = tag }, TestContext.Current.CancellationToken));
        Assert.Null(blob.Options);
        blob.Content.Dispose();
    }

    [Fact]
    public async Task CancellationBeforeClient()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new DocumentBlobDownloadService(Options.Create(DocumentIngestionTestData.Config),
            _ => throw new InvalidOperationException("No storage client should be requested."));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.OpenReadAsync(Claim(), cancellation.Token));
    }

    [Theory]
    [InlineData(404)]
    [InlineData(412)]
    [InlineData(503)]
    public async Task ProviderFailurePropagates(int status)
    {
        var blob = new ObservedBlob { FailureStatus = status };
        var exception = await Assert.ThrowsAsync<RequestFailedException>(() =>
            Create(blob).OpenReadAsync(Claim(), TestContext.Current.CancellationToken));
        Assert.Equal(status, exception.Status);
        blob.Content.Dispose();
    }

    private static DocumentBlobDownloadService Create(ObservedBlob blob) =>
        new(Options.Create(DocumentIngestionTestData.Config), container =>
        {
            Assert.Equal("https://storage.example.com/documents", container.AbsoluteUri);
            return new TestStorage(new ObservedContainer(blob));
        });

    private static DocumentIngestionClaim Claim() => new()
    {
        JobId = "job-1",
        ClaimId = Guid.NewGuid(),
        DocumentId = "document-1",
        TenantId = "tenant-a",
        CollectionId = "manuals",
        Sequencer = "1",
        BlobUri = "https://storage.example.com/documents/manual.pdf",
        ETag = "example-etag",
        ExpiresAtUtc = DateTimeOffset.MaxValue,
    };

    private sealed class TestStorage(BlobContainerClient containerClient) : AzBlobStorageBase(containerClient);

    private sealed class ObservedContainer(ObservedBlob blob) : BlobContainerClient
    {
        public override string Name => "documents";
        public override BlobClient GetBlobClient(string blobName)
        {
            blob.RequestedName = blobName;
            return blob;
        }
    }

    private sealed class ObservedBlob : BlobClient
    {
        public MemoryStream Content { get; } = new([1, 2, 3]);
        public string? RequestedName { get; set; }
        public BlobOpenReadOptions? Options { get; private set; }
        public CancellationToken Token { get; private set; }
        public int FailureStatus { get; init; }
        public override Task<Stream> OpenReadAsync(BlobOpenReadOptions options, CancellationToken cancellationToken = default)
        {
            Options = options;
            Token = cancellationToken;
            return FailureStatus == 0 ? Task.FromResult<Stream>(Content)
                : Task.FromException<Stream>(new RequestFailedException(FailureStatus, "Synthetic source failure."));
        }
    }
}
#endif
