#if DEBUG
using System.Security.Cryptography;
using System.Text.Json;
using UglyToad.PdfPig;

namespace CasCap.Tests.Unit;

/// <summary>Verifies paid-request bounds, prompt/input isolation, immutable recovery and private review output without credentials or network access.</summary>
[Trait("Category", "DocumentQuality")]
public sealed class OpenAIDocumentQualityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RequestContractAndCachedResponse(bool hybrid, bool unversionedModel)
    {
        var directory = TemporaryDirectory();
        var calls = 0;
        var input = Input(hybrid);
        using var http = Client(async (request, cancellationToken) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/openai/v1/responses", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement;
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.False(root.GetProperty("background").GetBoolean());
            Assert.Equal(8192, root.GetProperty("max_output_tokens").GetInt32());
            Assert.Equal("example-deployment", root.GetProperty("model").GetString());
            Assert.Contains("hyphenated list", root.GetProperty("instructions").GetString());
            Assert.Equal("json_schema", root.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
            var parts = root.GetProperty("input")[0].GetProperty("content");
            var encoded = parts[0].GetProperty("file_data").GetString()!;
            var bytes = Convert.FromBase64String(encoded["data:application/pdf;base64,".Length..]);
            Assert.Equal(input.Pdf, bytes);
            using var pdf = PdfDocument.Open(bytes);
            Assert.Equal(1, pdf.NumberOfPages);
            using var user = JsonDocument.Parse(parts[1].GetProperty("text").GetString()!);
            Assert.Equal(6, user.RootElement.GetProperty("physical_page").GetInt32());
            Assert.Equal(hybrid ? JsonValueKind.String : JsonValueKind.Null,
                user.RootElement.GetProperty("document_intelligence_draft").ValueKind);
            return Response(unversionedModel ? "unversioned" : "");
        });
        var client = new OpenAIDocumentQualityClient(http, Settings());
        try
        {
            var first = await client.AnalyzeAsync(directory, "sample", input, TestContext.Current.CancellationToken);
            var cached = await client.AnalyzeAsync(directory, "sample", input, TestContext.Current.CancellationToken);
            Assert.Equal(first.Markdown, cached.Markdown);
            Assert.Equal(20, cached.InputTokens);
            Assert.Equal(40, cached.OutputTokens);
            Assert.Equal(unversionedModel ? "gpt-5.5" : "gpt-5.5-2026-04-24", cached.Model);
            Assert.Equal(1, calls);
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.AnalyzeAsync(
                directory, "sample", input with { Draft = "Changed synthetic draft" }, TestContext.Current.CancellationToken));
            Assert.Equal(1, calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("http")]
    [InlineData("transport")]
    [InlineData("incomplete")]
    [InlineData("malformed")]
    [InlineData("refusal")]
    [InlineData("revision")]
    [InlineData("model")]
    [InlineData("tokens")]
    [InlineData("page")]
    [InlineData("empty")]
    public async Task FailureNeverReplays(string scenario)
    {
        var directory = TemporaryDirectory();
        var calls = 0;
        using var http = Client((_, _) =>
        {
            calls++;
            if (scenario == "transport")
                throw new HttpRequestException("Synthetic unknown outcome.");
            return Task.FromResult(Response(scenario));
        });
        var client = new OpenAIDocumentQualityClient(http, Settings());
        var input = Input(false);
        try
        {
            if (scenario == "transport")
                await Assert.ThrowsAsync<HttpRequestException>(() => Analyze());
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => Analyze());
            await Assert.ThrowsAsync<InvalidOperationException>(() => Analyze());
            Assert.Equal(1, calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Task<OpenAIDocumentQualityClient.Result> Analyze()
        {
            return client.AnalyzeAsync(directory, "sample", input, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task PreCancellationCreatesNoIntent()
    {
        var directory = TemporaryDirectory();
        using var http = Client((_, _) => throw new InvalidOperationException("No HTTP request expected."));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OpenAIDocumentQualityClient(http, Settings())
                .AnalyzeAsync(directory, "sample", Input(false), cancellation.Token));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://example.com/unexpected/")]
    public async Task InvalidEndpointCreatesNoIntent(string endpoint)
    {
        var directory = TemporaryDirectory();
        using var http = Client((_, _) => throw new InvalidOperationException("No HTTP request expected."));
        http.BaseAddress = new Uri(endpoint);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenAIDocumentQualityClient(http, Settings())
                .AnalyzeAsync(directory, "sample", Input(false), TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CopiesOnlySelectedPage()
    {
        var original = DocumentPreparationTestData.Pdf("FIRST source.", "SECOND source.", "THIRD source.");
        using var selected = PdfDocument.Open(OpenAIDocumentQualityClient.CopyPage(original, 2));
        Assert.Equal(1, selected.NumberOfPages);
        Assert.Contains("SECOND", selected.GetPage(1).Text);
        Assert.DoesNotContain("FIRST", selected.GetPage(1).Text);
        Assert.DoesNotContain("THIRD", selected.GetPage(1).Text);
        Assert.Throws<InvalidOperationException>(() => OpenAIDocumentQualityClient.CopyPage(original, 4));
    }

    [Theory]
    [InlineData("<table><tr><td>Wort</td><td>2</td></tr></table>", "- Wort: 2", 0, 0)]
    [InlineData("eins zwei zwei", "zwei eins", 0, 1)]
    [InlineData("Wort 2", "wort 3", 2, 2)]
    public void LexicalDifferences(string draft, string candidate, int added, int removed)
    {
        var differences = OpenAIDocumentQualityReview.Compare(draft, candidate);
        Assert.Equal(added, differences.Added.Values.Sum());
        Assert.Equal(removed, differences.Removed.Values.Sum());
    }

    [Fact]
    public async Task PrivateReviewEscapesContent()
    {
        var directory = TemporaryDirectory();
        const string text = "<img src=\"https://example.com/tracker\"> Synthetic!";
        var result = new OpenAIDocumentQualityClient.Result(text, ["Synthetic decision"], [], 20, 40, "example-model");
        try
        {
            await OpenAIDocumentQualityReview.WriteAsync(directory, "%PDF-synthetic"u8.ToArray(),
                [new(6, text, text, result, result)], TestContext.Current.CancellationToken);
            var html = await File.ReadAllTextAsync(Path.Combine(directory, "index.html"), TestContext.Current.CancellationToken);
            Assert.DoesNotContain("<img", html);
            Assert.Contains("&lt;img", html);
            Assert.Contains("value=\"hybrid\" selected", html);
            Assert.Contains("page-6-pdf-only.audit.json", html);
            Assert.Equal(text, await File.ReadAllTextAsync(Path.Combine(directory, "page-6-hybrid.md"), TestContext.Current.CancellationToken));
            Assert.True(File.Exists(Path.Combine(directory, "summary.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(13)]
    public async Task UnapprovedPageDoesNotSubmit(int page)
    {
        var directory = TemporaryDirectory();
        using var http = Client((_, _) => throw new InvalidOperationException("No HTTP request expected."));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenAIDocumentQualityClient(http, Settings())
                .AnalyzeAsync(directory, "sample", Input(false) with { Page = page }, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MultiPagePdfDoesNotSubmit()
    {
        var directory = TemporaryDirectory();
        using var http = Client((_, _) => throw new InvalidOperationException("No HTTP request expected."));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenAIDocumentQualityClient(http, Settings())
                .AnalyzeAsync(directory, "sample", Input(false) with { Pdf = DocumentPreparationTestData.Pdf("One.", "Two.") },
                    TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ResponseByteLimit()
    {
        var directory = TemporaryDirectory();
        using var http = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[DocumentIntelligenceQualityClient.MaximumResponseBytes + 1]),
        }));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenAIDocumentQualityClient(http, Settings())
                .AnalyzeAsync(directory, "sample", Input(false), TestContext.Current.CancellationToken));
            Assert.True(File.Exists(Path.Combine(directory, "sample.request.json")));
            Assert.False(File.Exists(Path.Combine(directory, "sample.response.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static OpenAIDocumentQualityClient.Input Input(bool hybrid)
    {
        var pdf = DocumentPreparationTestData.Pdf("Synthetic source.");
        return new(pdf, Convert.ToHexString(SHA256.HashData(pdf)), 6, hybrid ? "Synthetic draft" : null);
    }

    private static OpenAIDocumentQualityClient.Settings Settings() => new("example-deployment", "gpt-5.5", "2026-04-24");

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static HttpResponseMessage Response(string scenario = "")
    {
        var text = JsonSerializer.Serialize(new
        {
            page = scenario == "page" ? 7 : 6,
            markdown = scenario == "empty" ? "" : "Synthetic Markdown.",
            changes = Array.Empty<string>(),
            uncertainties = Array.Empty<string>(),
        });
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            status = scenario == "incomplete" ? "incomplete" : "completed",
            model = scenario switch
            {
                "unversioned" => "gpt-5.5",
                "revision" => "gpt-5.5-2026-01-01",
                "model" => "other-model",
                _ => "gpt-5.5-2026-04-24",
            },
            output = new[]
            {
                new
                {
                    type = "message", role = "assistant", status = "completed",
                    content = new[] { new { type = scenario == "refusal" ? "refusal" : "output_text", text } },
                },
            },
            usage = new { input_tokens = 20, output_tokens = scenario == "tokens" ? 8193 : 40 },
        });
        return new(scenario == "http" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(scenario == "malformed" ? "not JSON"u8.ToArray() : payload),
        };
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
