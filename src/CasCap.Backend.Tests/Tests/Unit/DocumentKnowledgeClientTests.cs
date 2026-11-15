using CasCap.AgentRuntime.Client;
using CasCap.AgentRuntime.Contracts.V1;
using System.Text;
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Checks escaped typed-client routes, exact query payloads and failure propagation without network access.</summary>
[Trait("Category", "Indexing")]
public sealed class DocumentKnowledgeClientTests
{
    [Theory]
    [InlineData(200, false)]
    [InlineData(404, false)]
    [InlineData(403, false)]
    [InlineData(502, false)]
    [InlineData(200, true)]
    [InlineData(404, true)]
    [InlineData(403, true)]
    [InlineData(502, true)]
    public async Task Client_UsesExplicitSearchAndProtectedSourceRoutesWithoutReplay(int status, bool citation)
    {
        using var handler = new Handler(status, citation);
        using var http = new HttpClient(handler) { BaseAddress = new("https://runtime.example.com/") };
        var client = new AgentRuntimeClient(http);
        async Task<object?> SendAsync() => citation
            ? await client.GetKnowledgePassageAsync("assistant one", "example-index", 2, TestContext.Current.CancellationToken)
            : await client.SearchKnowledgeAsync("assistant one", new() { Query = "synthetic query" }, TestContext.Current.CancellationToken);
        if (status is 403 or 502)
        {
            var failure = await Assert.ThrowsAsync<HttpRequestException>(SendAsync);
            Assert.Equal((HttpStatusCode)status, failure.StatusCode);
        }
        else
        {
            var result = await SendAsync();
            if (status == 404)
                Assert.Null(result);
            else if (citation)
                Assert.Equal("example-index", Assert.IsType<KnowledgePassageResponse>(result).IndexJobId);
            else
                Assert.Empty(Assert.IsType<SearchKnowledgeResponse>(result).Matches);
        }
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Client_RejectsEmptySuccessInsteadOfInventingMatches()
    {
        using var handler = new Handler(200, false, "null");
        using var http = new HttpClient(handler) { BaseAddress = new("https://runtime.example.com/") };
        var client = new AgentRuntimeClient(http);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.SearchKnowledgeAsync("assistant one", new() { Query = "synthetic query" }, TestContext.Current.CancellationToken));
    }

    private sealed class Handler(int status, bool citation, string? content = null) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal(citation ? HttpMethod.Get : HttpMethod.Post, request.Method);
            Assert.Equal("https://runtime.example.com/api/v1/agents/assistant%20one/knowledge/"
                + (citation ? "sources/example-index/chunks/2" : "search"), request.RequestUri!.AbsoluteUri);
            if (!citation)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal("synthetic query", body.RootElement.GetProperty("query").GetString());
                Assert.False(body.RootElement.TryGetProperty("tenantId", out _));
                Assert.False(body.RootElement.TryGetProperty("collectionId", out _));
            }
            return new((HttpStatusCode)status)
            {
                Content = new StringContent(content ?? (citation
                    ? """{"indexJobId":"example-index","documentId":"example-document","chunkOrdinal":2,"sourceSha256":"example-hash","cefrLevel":"A1","text":"synthetic passage","sourceSpans":[]}"""
                    : """{"matches":[],"embeddingInputTokens":0}"""), Encoding.UTF8, "application/json"),
            };
        }
    }
}
