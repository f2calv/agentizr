#if DEBUG
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CasCap.Tests.Unit;

/// <summary>Verifies durable-admission acknowledgement, source isolation and safe poison handling.</summary>
[Trait("Category", "Ingestion")]
public sealed class DocumentIngestionAdmissionTests
{
    [Fact]
    public async Task CommitBeforeAcknowledgement()
    {
        var queue = new FakeQueue(DocumentIngestionTestData.Body());
        var inbox = new FakeInbox
        {
            Accept = (notification, _) =>
            {
                Assert.Equal(0, queue.Acknowledgements);
                Assert.Equal("tenant-a", notification.TenantId);
                Assert.Equal("manuals", notification.CollectionId);
                return Task.CompletedTask;
            },
        };

        Assert.True(await Create(queue, inbox).ReceiveAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, inbox.Calls);
        Assert.Equal(1, queue.Acknowledgements);
        Assert.Equal(0, queue.Quarantined);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCommitIsNotAcknowledged(bool argumentFailure)
    {
        var queue = new FakeQueue(DocumentIngestionTestData.Body());
        var inbox = new FakeInbox
        {
            Accept = (_, _) => argumentFailure
                ? throw new ArgumentException("synthetic application failure")
                : throw new InvalidOperationException("synthetic database failure"),
        };

        if (argumentFailure)
            await Assert.ThrowsAsync<ArgumentException>(() =>
                Create(queue, inbox).ReceiveAsync(TestContext.Current.CancellationToken));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Create(queue, inbox).ReceiveAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, queue.Acknowledgements);
        Assert.Equal(0, queue.Quarantined);
    }

    [Fact]
    public async Task LeaseLossPreventsAcknowledgement()
    {
        using var ownership = new CancellationTokenSource();
        var queue = new FakeQueue(DocumentIngestionTestData.Body());
        var inbox = new FakeInbox
        {
            Accept = async (_, token) =>
            {
                await ownership.CancelAsync();
                token.ThrowIfCancellationRequested();
            },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(queue, inbox).ReceiveAsync(ownership.Token));
        Assert.Equal(0, queue.Acknowledgements);
    }

    [Theory]
    [InlineData("/unknown/storage", "https://storage.example.com/documents/manual.pdf")]
    [InlineData("/example/storage", "https://storage.example.com/other/manual.pdf")]
    [InlineData("/example/storage", "https://storage.example.com/documents-other/manual.pdf")]
    [InlineData("/example/storage", "https://storage.example.com/documents/wrong-name.pdf")]
    [InlineData("/example/storage", "https://storage.example.com/documents/manual.pdf?sig=invalid")]
    public async Task UnassignedSourceIsQuarantined(string topic, string url)
    {
        var queue = new FakeQueue(DocumentIngestionTestData.Body(topic: topic, url: url));
        var inbox = new FakeInbox();
        Assert.True(await Create(queue, inbox).ReceiveAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, queue.Quarantined);
        Assert.Equal(0, inbox.Calls);
        Assert.Equal(0, queue.Acknowledgements);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task InvalidEnvelopeIsQuarantined(string body)
    {
        var queue = new FakeQueue(BinaryData.FromString(body));
        var inbox = new FakeInbox();
        Assert.True(await Create(queue, inbox).ReceiveAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, queue.Quarantined);
        Assert.Equal(0, inbox.Calls);
    }

    [Theory]
    [InlineData("1", "2", true)]
    [InlineData("0f", "10", false)]
    public void StorageOrderingToken(string first, string second, bool deleted)
    {
        var a = BlobStorageEventReader.Read(DocumentIngestionTestData.Body(sequencer: first, deleted: deleted));
        var b = BlobStorageEventReader.Read(DocumentIngestionTestData.Body(sequencer: second));
        Assert.True(string.CompareOrdinal(a.Sequencer, b.Sequencer) < 0);
        Assert.Equal(128, a.Sequencer.Length);
        Assert.Equal(deleted, a.Deleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-hex")]
    public void InvalidOrderingToken(string sequencer) =>
        Assert.Throws<JsonException>(() => BlobStorageEventReader.Read(DocumentIngestionTestData.Body(sequencer: sequencer)));

    [Fact]
    public void SingletonArrayEnvelope()
    {
        var body = DocumentIngestionTestData.Body();
        Assert.Equal(BlobStorageEventReader.Read(body),
            BlobStorageEventReader.Read(BinaryData.FromString($"[{body}]")));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    public async Task InvalidDataObjectIsQuarantined(string data)
    {
        var envelope = JsonNode.Parse(DocumentIngestionTestData.Body().ToString())!;
        envelope["data"] = JsonNode.Parse(data);
        var queue = new FakeQueue(BinaryData.FromString(envelope.ToJsonString()));
        var inbox = new FakeInbox();
        Assert.True(await Create(queue, inbox).ReceiveAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, queue.Quarantined);
        Assert.Equal(0, inbox.Calls);
    }

    private static DocumentIngestionAdmissionService Create(FakeQueue queue, FakeInbox inbox) =>
        new(NullLogger<DocumentIngestionAdmissionService>.Instance, Options.Create(DocumentIngestionTestData.Config), queue, inbox);

    private sealed class FakeQueue(BinaryData body) : IDocumentIngestionEventQueue
    {
        public int Acknowledgements { get; private set; }
        public int Quarantined { get; private set; }
        public Task<IngestionQueueMessage?> ReceiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IngestionQueueMessage?>(new() { Id = "message", LeaseToken = "lease", Body = body });
        public Task<IngestionQueueMessage> RenewAsync(IngestionQueueMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(message);
        public Task AcknowledgeAsync(IngestionQueueMessage message, CancellationToken cancellationToken)
        {
            Acknowledgements++;
            return Task.CompletedTask;
        }
        public Task ReleaseAsync(IngestionQueueMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QuarantineAsync(IngestionQueueMessage message, CancellationToken cancellationToken)
        {
            Quarantined++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInbox : IDocumentIngestionInbox
    {
        public Func<DocumentIngestionNotification, CancellationToken, Task> Accept { get; init; } = (_, _) => Task.CompletedTask;
        public int Calls { get; private set; }
        public Task AcceptAsync(DocumentIngestionNotification notification, CancellationToken cancellationToken)
        {
            Calls++;
            return Accept(notification, cancellationToken);
        }
    }
}
#endif
