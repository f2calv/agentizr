#if DEBUG
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Verifies bounded paid submissions, safe polling destinations and strict private-result validation without network access.</summary>
[Trait("Category", "DocumentQuality")]
public sealed class DocumentIntelligenceQualityClientTests
{
    private const string Operation = "https://example.com/documentintelligence/documentModels/prebuilt-layout/analyzeResults/00000000-0000-0000-0000-000000000001?api-version=2024-11-30";

    [Fact]
    public async Task SubmissionContract()
    {
        var calls = 0;
        using var http = Client(async (request, cancellationToken) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("pages=2,3,6", request.RequestUri!.Query);
            Assert.Contains("outputContentFormat=markdown", request.RequestUri.Query);
            Assert.Contains("stringIndexType=utf16CodeUnit", request.RequestUri.Query);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(Convert.ToBase64String("%PDF-synthetic"u8), json.RootElement.GetProperty("base64Source").GetString());
            var response = new HttpResponseMessage(HttpStatusCode.Accepted);
            response.Headers.Add("Operation-Location", Operation);
            return response;
        });
        var operation = await new DocumentIntelligenceQualityClient(http).SubmitAsync(
            "%PDF-synthetic"u8.ToArray(), [2, 3, 6], TestContext.Current.CancellationToken);
        Assert.Equal(new Uri(Operation), operation);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task SubmissionIsNotReplayed(int status)
    {
        var calls = 0;
        using var http = Client((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DocumentIntelligenceQualityClient(http)
            .SubmitAsync("%PDF-synthetic"u8.ToArray(), [1], TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AmbiguousSubmissionIsNotReplayed()
    {
        var calls = 0;
        using var http = Client((_, _) =>
        {
            calls++;
            throw new HttpRequestException("Synthetic transport failure.");
        });
        await Assert.ThrowsAsync<HttpRequestException>(() => new DocumentIntelligenceQualityClient(http)
            .SubmitAsync("%PDF-synthetic"u8.ToArray(), [1], TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("https://other.example.com/documentintelligence/documentModels/prebuilt-layout/analyzeResults/00000000-0000-0000-0000-000000000001?api-version=2024-11-30")]
    [InlineData("http://example.com/documentintelligence/documentModels/prebuilt-layout/analyzeResults/00000000-0000-0000-0000-000000000001?api-version=2024-11-30")]
    [InlineData("https://example.com/unexpected?api-version=2024-11-30")]
    public async Task PollDestinationRejected(string location)
    {
        var calls = 0;
        using var http = Client((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DocumentIntelligenceQualityClient(http)
            .PollAsync(new Uri(location), [1], TestContext.Current.CancellationToken));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ReceiptCanResumeWithoutSubmission()
    {
        using var http = Client((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Payload([2, 3, 6])),
            });
        });
        var payload = await new DocumentIntelligenceQualityClient(http).PollAsync(new Uri(Operation), [2, 3, 6],
            TestContext.Current.CancellationToken);
        var result = DocumentIntelligenceQualityClient.ValidateResult(payload, [2, 3, 6]);
        Assert.Equal(3, result.Pages.Count);
        Assert.Equal("Synthetic \U0001F600 text.", result.Markdown);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("canceled")]
    [InlineData("unexpected")]
    public async Task UnsuccessfulPollingIsNotSuccess(string status)
    {
        using var http = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { status })),
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DocumentIntelligenceQualityClient(http)
            .PollAsync(new Uri(Operation), [1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancellationDoesNotSend()
    {
        var calls = 0;
        using var http = Client((_, _) =>
        {
            calls++;
            throw new InvalidOperationException("No request was expected.");
        });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DocumentIntelligenceQualityClient(http)
            .PollAsync(new Uri(Operation), [1], cancellation.Token));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ReceiptSurvivesPollingFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var submissions = 0;
        var polls = 0;
        using var http = Client((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                submissions++;
                var accepted = new HttpResponseMessage(HttpStatusCode.Accepted);
                accepted.Headers.Add("Operation-Location", Operation);
                return Task.FromResult(accepted);
            }
            polls++;
            return Task.FromResult(new HttpResponseMessage(polls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Payload([1])),
            });
        });
        var client = new DocumentIntelligenceQualityClient(http);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Analyze());
            Assert.True(File.Exists(Path.Combine(directory, "sample.operation.json")));
            var result = await Analyze();
            Assert.NotEmpty(result);
            Assert.Equal(1, submissions);
            Assert.Equal(2, polls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Task<byte[]> Analyze()
        {
            return DocumentIntelligenceBenchmarkState.AnalyzeAsync(client, directory, "sample",
                "%PDF-synthetic"u8.ToArray(), [1], TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InvalidResultsRejected(int variant)
    {
        var payload = variant switch
        {
            0 => "{}"u8.ToArray(),
            1 => "not JSON"u8.ToArray(),
            2 => Payload([1, 1]),
            3 => Payload([2]),
            _ => Payload([1], invalidSpan: true),
        };
        Assert.Throws<InvalidOperationException>(() => DocumentIntelligenceQualityClient.ValidateResult(payload, [1]));
    }

    [Fact]
    public async Task InvalidPageBudgetsDoNotSubmit()
    {
        using var http = Client((_, _) => throw new InvalidOperationException("No request was expected."));
        var client = new DocumentIntelligenceQualityClient(http);
        foreach (var pages in new[] { Array.Empty<int>(), new[] { 0 }, new[] { 1, 1 }, Enumerable.Range(1, 7).ToArray() })
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.SubmitAsync("%PDF-synthetic"u8.ToArray(), pages, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistentSubmissionState(bool ambiguous)
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var calls = 0;
        using var http = Client((request, _) =>
        {
            calls++;
            if (ambiguous)
                throw new HttpRequestException("Synthetic ambiguous submission.");
            var response = new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Accepted : HttpStatusCode.OK);
            if (request.Method == HttpMethod.Post)
                response.Headers.Add("Operation-Location", Operation);
            else
                response.Content = new ByteArrayContent(Payload([1]));
            return Task.FromResult(response);
        });
        var client = new DocumentIntelligenceQualityClient(http);
        try
        {
            if (ambiguous)
            {
                await Assert.ThrowsAsync<HttpRequestException>(() => Analyze());
                await Assert.ThrowsAsync<InvalidOperationException>(() => Analyze());
                Assert.Equal(1, calls);
            }
            else
            {
                var first = await Analyze();
                var second = await Analyze();
                Assert.Equal(first, second);
                Assert.Equal(2, calls);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    DocumentIntelligenceBenchmarkState.AnalyzeAsync(client, directory, "sample",
                        "%PDF-changed"u8.ToArray(), [1], TestContext.Current.CancellationToken));
                Assert.Equal(2, calls);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Task<byte[]> Analyze()
        {
            return DocumentIntelligenceBenchmarkState.AnalyzeAsync(client, directory, "sample",
                "%PDF-synthetic"u8.ToArray(), [1], TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"status\":1}")]
    [InlineData("{\"status\":\"succeeded\",\"analyzeResult\":1}")]
    public void MalformedTypes(string json) =>
        Assert.Throws<InvalidOperationException>(() => DocumentIntelligenceQualityClient.ValidateResult(
            System.Text.Encoding.UTF8.GetBytes(json), [1]));

    [Fact]
    public async Task ResponseLimit()
    {
        using var http = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[DocumentIntelligenceQualityClient.MaximumResponseBytes + 1]),
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DocumentIntelligenceQualityClient(http)
            .PollAsync(new Uri(Operation), [1], TestContext.Current.CancellationToken));
    }

    private static byte[] Payload(int[] pages, bool invalidSpan = false)
    {
        const string content = "Synthetic \U0001F600 text.";
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            status = "succeeded",
            analyzeResult = new
            {
                apiVersion = DocumentIntelligenceQualityClient.ApiVersion,
                modelId = DocumentIntelligenceQualityClient.ModelId,
                contentFormat = "markdown",
                content,
                pages = pages.Select(page => new
                {
                    pageNumber = page,
                    spans = new[] { new { offset = 0, length = invalidSpan ? content.Length + 1 : content.Length } },
                }),
            },
        });
    }

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new StubHandler(send)) { BaseAddress = new Uri("https://example.com/") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
#endif
