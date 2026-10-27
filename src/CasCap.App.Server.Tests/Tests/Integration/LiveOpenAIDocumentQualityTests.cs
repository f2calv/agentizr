#if DEBUG
using Azure;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using UglyToad.PdfPig;

namespace CasCap.Tests.Integration;

/// <summary>Runs six explicitly authorized gpt-5.5 PDF-only/hybrid requests on cached vocabulary pages, without production activation.</summary>
[Trait("Category", "Integration")]
public sealed class LiveOpenAIDocumentQualityTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    [Trait("Category", "OpenAIDocumentBenchmark")]
    public async Task PdfOnlyAndHybridComparison()
    {
        Assert.SkipUnless(_configuration.GetValue<bool>("DocumentQuality:OpenAIBenchmarkEnabled"),
            "Set DocumentQuality__OpenAIBenchmarkEnabled=true only after approving six paid OpenAI requests and the selected model.");
        Assert.SkipUnless(_configuration.GetValue<bool>("DocumentQuality:LiveEnabled"), "The live document-quality gate must also be enabled.");
        try
        {
            await RunAsync(TestContext.Current.CancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("OpenAI transport failed; preserve this fixed directory. An incomplete request cannot be automatically resubmitted.");
        }
        catch (AuthenticationFailedException)
        {
            throw new InvalidOperationException("OpenAI application-identity authentication failed; details withheld. No fallback credential or model was used.");
        }
        catch (RequestFailedException exception)
        {
            throw new InvalidOperationException($"OpenAI identity/service access failed with HTTP {exception.Status}; details withheld.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Private benchmark source/state JSON is invalid; details withheld.");
        }
    }

    [Fact]
    [Trait("Category", "OpenAIDocumentAccess")]
    public async Task ReadOnlyServiceAccess()
    {
        Assert.SkipUnless(_configuration.GetValue<bool>("DocumentQuality:OpenAIBenchmarkEnabled")
            && _configuration.GetValue<bool>("DocumentQuality:LiveEnabled"), "Enable the separately approved OpenAI/live gates for read-only access checks.");
        await using var transport = CreateTransport(Endpoint());
        using var http = transport.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenAIDocumentQualityClient));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var response = await http.GetAsync("openai/v1/models", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            _output.WriteLine($"Read-only OpenAI model-list access returned HTTP {(int)response.StatusCode}; no generation was submitted.");
            if (response.StatusCode != HttpStatusCode.OK)
            {
                await response.Content.LoadIntoBufferAsync(16 * 1024, deadline.Token);
                using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(deadline.Token));
                var code = json.RootElement.GetProperty("error").GetProperty("code").GetString();
                var safeCode = code is "SubscriptionNotRegistered" or "PermissionDenied" or "Unauthorized"
                    or "InvalidApiVersionParameter" or "UnsupportedApiVersion" ? code : "UnclassifiedError";
                throw new InvalidOperationException($"Read-only OpenAI access rejected HTTP {(int)response.StatusCode}; category={safeCode}; details withheld.");
            }
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Read-only OpenAI transport failed; details withheld.");
        }
        catch (AuthenticationFailedException)
        {
            throw new InvalidOperationException("Read-only OpenAI application-identity authentication failed; details withheld.");
        }
        catch (RequestFailedException exception)
        {
            throw new InvalidOperationException($"Read-only OpenAI identity/service access failed with HTTP {exception.Status}; details withheld.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Read-only OpenAI error response was not valid JSON; details withheld.");
        }
        finally
        {
            await deadline.CancelAsync();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var managedDirectory = PrivateDirectory("ManagedOutputDirectory");
        var localDirectory = PrivateDirectory("ManagedSourceDirectory");
        var outputDirectory = PrivateDirectory("OpenAIOutputDirectory");
        if (new[] { managedDirectory, localDirectory, outputDirectory }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
            throw new InvalidOperationException("OpenAI comparisons require a distinct fixed private output directory.");
        var settings = new OpenAIDocumentQualityClient.Settings(
            Required("OpenAIDeployment"), "gpt-5.5", Required("OpenAIModelVersion"));
        var endpoint = Endpoint();
        var originalPath = Path.Combine(managedDirectory, "source-2-sample-2.pdf");
        if (new FileInfo(originalPath).Length is < 5 or > 20 * 1024 * 1024)
            throw new InvalidOperationException("Cached original exceeds the approved PDF byte budget.");
        var original = await File.ReadAllBytesAsync(originalPath, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(original));
        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(managedDirectory, "source-2-sample-2.request.json"), cancellationToken));
        if (manifest.RootElement.GetProperty("SourceSha256").GetString() != hash
            || !manifest.RootElement.GetProperty("Pages").EnumerateArray().Select(page => page.GetInt32()).SequenceEqual([6, 7, 12]))
            throw new InvalidOperationException("Cached managed request differs from the approved original/page plan.");
        var managed = DocumentIntelligenceQualityClient.ValidateResult(await File.ReadAllBytesAsync(
            Path.Combine(managedDirectory, "source-2-sample-2.analysis.json"), cancellationToken), [6, 7, 12]);
        using var layout = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(localDirectory, "source-2-sample-2.layout.json"), cancellationToken));
        var localPages = layout.RootElement.EnumerateArray().Where(page => new[] { 6, 7, 12 }.Contains(page.GetProperty("Page").GetInt32()))
            .ToDictionary(page => page.GetProperty("Page").GetInt32(), page =>
            {
                if (!string.Equals(page.GetProperty("SourceSha256").GetString(), hash, StringComparison.OrdinalIgnoreCase)
                    || page.GetProperty("LayoutAlgorithm").GetString() != DocumentLayoutReview.Version)
                    throw new InvalidOperationException("Local comparison source/profile differs from the cached managed original.");
                return page.GetProperty("Layout").GetProperty("Sections").GetString()!;
            });
        if (localPages.Count != 3 || localPages.Values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Selected local comparison pages are missing.");
        Directory.CreateDirectory(outputDirectory);
        var inputs = new Dictionary<int, byte[]>();
        foreach (var page in new[] { 6, 7, 12 })
        {
            var file = Path.Combine(outputDirectory, $"page-{page}.pdf");
            var sourcePin = Path.Combine(outputDirectory, $"page-{page}.source.json");
            var existing = File.Exists(file);
            if (existing != File.Exists(sourcePin))
                throw new InvalidOperationException("Copied page has incomplete provenance state; reconcile before submission.");
            var pdf = existing ? await File.ReadAllBytesAsync(file, cancellationToken) : OpenAIDocumentQualityClient.CopyPage(original, page);
            var pin = JsonSerializer.SerializeToUtf8Bytes(new { OriginalSha256 = hash, Page = page, DerivedSha256 = Convert.ToHexString(SHA256.HashData(pdf)) });
            await DocumentIntelligenceBenchmarkReview.SaveAsync(sourcePin, pin, cancellationToken);
            await DocumentIntelligenceBenchmarkReview.SaveAsync(file, pdf, cancellationToken);
            using var checkedPage = PdfDocument.Open(pdf);
            if (checkedPage.NumberOfPages != 1)
                throw new InvalidOperationException("Copied request PDF is not exactly one page.");
            inputs.Add(page, pdf);
        }
        await using var transport = CreateTransport(endpoint);
        using var http = transport.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenAIDocumentQualityClient));
        var client = new OpenAIDocumentQualityClient(http, settings);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            var pages = new List<OpenAIDocumentQualityReview.Page>();
            foreach (var (page, pdf) in inputs)
            {
                var elapsed = Stopwatch.StartNew();
                var pdfOnly = await client.AnalyzeAsync(outputDirectory, $"page-{page}-pdf-only", new(pdf, hash, page, null), deadline.Token);
                var hybrid = await client.AnalyzeAsync(outputDirectory, $"page-{page}-hybrid", new(pdf, hash, page, managed.Pages[page]), deadline.Token);
                pages.Add(new(page, localPages[page], managed.Pages[page], pdfOnly, hybrid));
                _output.WriteLine($"Physical page={page}; candidates=2; inputTokens={pdfOnly.InputTokens + hybrid.InputTokens}; outputTokens={pdfOnly.OutputTokens + hybrid.OutputTokens}; elapsedSeconds={elapsed.Elapsed.TotalSeconds:F1}.");
            }
            await OpenAIDocumentQualityReview.WriteAsync(outputDirectory, original, pages, deadline.Token);
            Assert.Equal(3, pages.Count);
        }
        finally
        {
            await deadline.CancelAsync();
        }
    }

    private string Required(string key) =>
        _configuration["DocumentQuality:" + key] is { Length: > 0 } value ? value
            : throw new InvalidOperationException("Configure the required private OpenAI benchmark setting.");

    private Uri Endpoint()
    {
        if (!Uri.TryCreate(Required("OpenAIEndpoint"), UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps || endpoint.Port != 443 || endpoint.AbsolutePath != "/"
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0)
            throw new InvalidOperationException("Configure the approved Azure OpenAI HTTPS root endpoint.");
        return endpoint;
    }

    private ServiceProvider CreateTransport(Uri endpoint)
    {
        var runtime = InitializeLiveServices();
        var services = new ServiceCollection();
        services.AddHttpClient(nameof(OpenAIDocumentQualityClient), client =>
        {
            client.BaseAddress = endpoint;
            client.Timeout = TimeSpan.FromMinutes(3);
        }).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .AddHttpMessageHandler(() => new TokenCredentialBearerHandler(
                runtime.GetRequiredService<RuntimeEntraCredential>().Credential!, "https://ai.azure.com/.default"));
        return services.BuildServiceProvider();
    }

    private string PrivateDirectory(string key)
    {
        var value = Required(key);
        if (!Path.IsPathFullyQualified(value))
            throw new InvalidOperationException("Benchmark paths must be absolute private directories.");
        var path = Path.GetFullPath(value);
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git")))
                throw new InvalidOperationException("Private benchmark artifacts must be outside all Git checkouts.");
        return path;
    }
}
#endif
