#if DEBUG
using Azure;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Runs only the explicitly approved two-source, six-page charged benchmark; never starts ingestion or indexes extracted text.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "DocumentIntelligenceBenchmark")]
public sealed class LiveDocumentIntelligenceQualityTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task ManagedLayoutComparison()
    {
        Assert.SkipUnless(_configuration.GetValue<bool>("DocumentQuality:ManagedBenchmarkEnabled"),
            "Set DocumentQuality__ManagedBenchmarkEnabled=true only after approving Azure submission and charges for two PDFs/six pages.");
        var sourceDirectory = PrivateDirectory("ManagedSourceDirectory");
        var outputDirectory = PrivateDirectory("ManagedOutputDirectory");
        if (string.Equals(sourceDirectory, outputDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Managed comparisons require a separate private output directory.");
        var samples = new[]
        {
            await LoadAsync(sourceDirectory, "source-1-sample-1", [2, 3, 6], TestContext.Current.CancellationToken),
            await LoadAsync(sourceDirectory, "source-2-sample-2", [6, 7, 12], TestContext.Current.CancellationToken),
        };
        var endpoint = _configuration["DocumentQuality:DocumentIntelligenceEndpoint"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseAddress) || baseAddress.Scheme != Uri.UriSchemeHttps
            || baseAddress.AbsolutePath != "/" || baseAddress.Port != 443 || baseAddress.Query.Length != 0
            || baseAddress.Fragment.Length != 0 || baseAddress.UserInfo.Length != 0)
            throw new InvalidOperationException("Configure the approved Document Intelligence HTTPS root endpoint.");
        Directory.CreateDirectory(outputDirectory);
        var runtime = InitializeLiveServices();
        var credential = runtime.GetRequiredService<RuntimeEntraCredential>();
        var services = new ServiceCollection();
        services.AddHttpClient(nameof(DocumentIntelligenceQualityClient), client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(90);
        }).RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .AddHttpMessageHandler(() => new TokenCredentialBearerHandler(
                credential.Credential!, "https://cognitiveservices.azure.com/.default"));
        await using var transport = services.BuildServiceProvider();
        using var http = transport.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(DocumentIntelligenceQualityClient));
        var client = new DocumentIntelligenceQualityClient(http);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(8));
        try
        {
            var measurements = new List<Measurement>();
            foreach (var sample in samples)
            {
                var reused = File.Exists(Path.Combine(outputDirectory, sample.Stem + ".analysis.json"));
                var submitted = !File.Exists(Path.Combine(outputDirectory, sample.Stem + ".request.json"));
                var elapsed = Stopwatch.StartNew();
                var payload = await DocumentIntelligenceBenchmarkState.AnalyzeAsync(
                    client, outputDirectory, sample.Stem, sample.Original, sample.Pages, deadline.Token);
                var managed = DocumentIntelligenceQualityClient.ValidateResult(payload, sample.Pages);
                Assert.Equal(sample.Pages.Order(), managed.Pages.Keys.Order());
                await DocumentIntelligenceBenchmarkReview.WriteAsync(outputDirectory, sample.Stem,
                    sample.Original, sample.LocalPages, managed, deadline.Token);
                measurements.Add(new(sample.Pages, managed.Pages.Count, sample.Original.Length,
                    managed.Markdown.Length, elapsed.ElapsedMilliseconds, reused, submitted));
                _output.WriteLine($"Sample completed: pages={managed.Pages.Count}; MarkdownCharacters={managed.Markdown.Length}; elapsedSeconds={elapsed.Elapsed.TotalSeconds:F1}.");
            }
            var metricsPath = Path.Combine(outputDirectory, "metrics.json");
            if (!File.Exists(metricsPath))
            {
                await using var metrics = new FileStream(metricsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, useAsync: true);
                await JsonSerializer.SerializeAsync(metrics, measurements, cancellationToken: deadline.Token);
            }
            await DocumentIntelligenceBenchmarkReview.WriteIndexAsync(outputDirectory, deadline.Token);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Managed transport failed; details withheld. Reuse this directory/receipt; never automatically resubmit.");
        }
        catch (AuthenticationFailedException)
        {
            throw new InvalidOperationException("Managed app-identity authentication failed; details withheld. Check RBAC before reconciling submission state.");
        }
        catch (RequestFailedException exception)
        {
            throw new InvalidOperationException($"Managed identity/service request failed with HTTP {exception.Status}; details withheld.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Private benchmark input/receipt JSON is invalid; details withheld.");
        }
        finally
        {
            await deadline.CancelAsync();
        }
    }

    private string PrivateDirectory(string key)
    {
        var value = _configuration["DocumentQuality:" + key];
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw new InvalidOperationException("Configure an absolute private managed benchmark directory.");
        var path = Path.GetFullPath(value);
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git")))
                throw new InvalidOperationException("Managed benchmark artifacts must remain outside every Git checkout.");
        return path;
    }

    private static async Task<Sample> LoadAsync(string directory, string stem, int[] selectedPages, CancellationToken cancellationToken)
    {
        var pdfPath = Path.Combine(directory, stem + ".pdf");
        if (new FileInfo(pdfPath).Length is < 5 or > 20 * 1024 * 1024)
            throw new InvalidOperationException("Benchmark source violates the approved PDF byte budget.");
        var original = await File.ReadAllBytesAsync(pdfPath, cancellationToken);
        if (!original.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw new InvalidOperationException("Benchmark source is not a PDF.");
        var hash = Convert.ToHexString(SHA256.HashData(original));
        using var layout = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory, stem + ".layout.json"), cancellationToken));
        var localPages = new Dictionary<int, string>();
        foreach (var page in layout.RootElement.EnumerateArray())
        {
            if (!string.Equals(hash, page.GetProperty("SourceSha256").GetString(), StringComparison.OrdinalIgnoreCase)
                || page.GetProperty("LayoutAlgorithm").GetString() != DocumentLayoutReview.Version)
                throw new InvalidOperationException("Cached original differs from the approved local comparison provenance.");
            var number = page.GetProperty("Page").GetInt32();
            if (selectedPages.Contains(number))
                localPages.Add(number, page.GetProperty("Layout").GetProperty("Sections").GetString()!);
        }
        if (localPages.Count != selectedPages.Length || localPages.Values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Cached local review omits an approved comparison page.");
        return new(stem, original, selectedPages, localPages);
    }

    private sealed record Sample(string Stem, byte[] Original, int[] Pages, IReadOnlyDictionary<int, string> LocalPages);

    private sealed record Measurement(
        int[] RequestedPages,
        int ReturnedPageCount,
        int OriginalBytes,
        int MarkdownCharacters,
        long ElapsedMilliseconds,
        bool ReusedResult,
        bool SubmissionAttempted);
}
#endif
