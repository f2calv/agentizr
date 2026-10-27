#if DEBUG
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.ML.Tokenizers;
using System.Security.Cryptography;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace CasCap.Tests.Integration;

/// <summary>Reads only explicitly configured containers through the real conditional downloader and PDF pipeline.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "DocumentQualityLive")]
public sealed class LiveDocumentQualityTests(ITestOutputHelper output) : TestBase(output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BlobDownload_Prepare_ValidateQuality(bool semanticExpectations) =>
        ValidateDocumentsAsync(semanticExpectations, previewDirectory: null);

    [Fact]
    [Trait("Category", "DocumentQualityPreview")]
    public async Task CreatePrivateReviewPreviews()
    {
        Assert.SkipUnless(_configuration.GetValue<bool>("DocumentQuality:PreviewEnabled"),
            "Saving private PDF/text previews requires DocumentQuality__PreviewEnabled=true.");
        Assert.SkipUnless(_configuration.GetValue<bool>("DocumentQuality:LiveEnabled"),
            "Private preview generation also requires DocumentQuality__LiveEnabled=true.");
        var directory = _configuration["DocumentQuality:PreviewDirectory"];
        Assert.True(!string.IsNullOrWhiteSpace(directory) && Path.IsPathFullyQualified(directory),
            "Configure an absolute, private DocumentQuality:PreviewDirectory outside Git worktrees.");
        for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
            Assert.False(Directory.Exists(Path.Combine(parent.FullName, ".git")) || File.Exists(Path.Combine(parent.FullName, ".git")),
                "Private review artifacts must be outside Git worktrees.");
        var runDirectory = Path.Combine(directory, $"review-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runDirectory);
        await ValidateDocumentsAsync(semanticExpectations: false, runDirectory);
        _output.WriteLine("Private review bundle created; originals and extracted content were not written to test output.");
    }

    private async Task ValidateDocumentsAsync(bool semanticExpectations, string? previewDirectory)
    {
        if (semanticExpectations)
            Assert.SkipUnless(_configuration.GetValue<bool>("DocumentQuality:SemanticEnabled"),
                "Independent, hash-pinned passage/order expectations require DocumentQuality__SemanticEnabled=true.");
        var services = InitializeLiveServices();
        var sources = services.GetRequiredService<IOptions<DocumentIngestionConfig>>().Value.Sources;
        Assert.True(sources.Count > 0, "Configure explicitly authorized DocumentIngestionConfig:Sources.");
        var credential = services.GetRequiredService<RuntimeEntraCredential>().Credential!;
        var download = services.GetRequiredService<DocumentBlobDownloadService>();
        var preparation = services.GetRequiredService<PdfDocumentPreparationService>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var previewStems = new List<string>();
        try
        {
            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var source = sources[sourceIndex];
                Assert.True(Uri.TryCreate(source.ContainerUri, UriKind.Absolute, out var containerUri)
                    && containerUri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(containerUri.Query)
                    && string.IsNullOrEmpty(containerUri.UserInfo) && string.IsNullOrEmpty(containerUri.Fragment)
                    && containerUri.Segments.Length == 2
                    && string.Equals(source.ContainerUri.TrimEnd('/'), containerUri.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal),
                    "A canonical credential-free HTTPS source container is required.");
                var container = new BlobContainerClient(containerUri, credential);
                var selected = await SelectDocumentsAsync(container, sourceIndex, semanticExpectations, deadline.Token);
                foreach (var blob in selected)
                {
                    var claim = new DocumentIngestionClaim
                    {
                        JobId = "quality-probe",
                        ClaimId = Guid.NewGuid(),
                        DocumentId = "quality-probe",
                        TenantId = source.TenantId,
                        CollectionId = source.CollectionId,
                        BlobUri = container.GetBlobClient(blob.Name).Uri.AbsoluteUri,
                        ETag = blob.ETag,
                        Sequencer = "1",
                        ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
                    };
                    PdfPreparationResult result;
                    await using (var stream = await download.OpenReadAsync(claim, deadline.Token))
                        result = await preparation.PrepareAsync(stream, deadline.Token);
                    Assert.Equal(PdfPreparationStatus.Prepared, result.Status);
                    Assert.NotNull(result.SourceSha256);
                    Assert.NotEmpty(result.Chunks);
                    Assert.Equal(Enumerable.Range(0, result.Chunks.Count), result.Chunks.Select(chunk => chunk.Ordinal));
                    Assert.All(result.Chunks, chunk =>
                    {
                        Assert.InRange(chunk.PageNumber, 1, result.PageCount);
                        Assert.InRange(chunk.TokenCount, 1, result.Profile.MaximumTokensPerChunk);
                        Assert.False(string.IsNullOrWhiteSpace(chunk.Text));
                        Assert.False(chunk.Text.Contains('\uFFFD'), "Extracted text contains a Unicode replacement character; text withheld.");
                    });
                    _output.WriteLine($"Source {sourceIndex + 1}: conditional download and bounded preparation passed.");
                    var pages = await CompareLayoutAsync(download, claim, result,
                        services.GetRequiredService<IOptions<DocumentPreparationConfig>>().Value.MaximumPdfBytes, deadline.Token);
                    if (semanticExpectations)
                        AssertQualityExpectations(result, sourceIndex, pages.Pages.Select(page => page.ContentOrderText).ToList());
                    if (previewDirectory is not null)
                    {
                        var stem = $"source-{sourceIndex + 1}-sample-{previewStems.Count + 1}";
                        await DocumentQualityReviewWriter.WriteSourceAsync(previewDirectory, stem, pages.Original,
                            pages.Pages, result, deadline.Token);
                        previewStems.Add(stem);
                    }
                }
            }
            if (previewDirectory is not null)
                await DocumentQualityReviewWriter.WriteIndexAsync(previewDirectory, previewStems, deadline.Token);
        }
        // SDK diagnostics can contain private endpoints; expose only the safe failure category.
        catch (RequestFailedException exception)
        {
            throw new InvalidOperationException($"Read-only Blob operation failed with HTTP status {exception.Status}; details withheld.");
        }
        catch (AuthenticationFailedException)
        {
            throw new InvalidOperationException("Blob identity authentication failed; details withheld.");
        }
        catch (IOException)
        {
            throw new InvalidOperationException("Document reading or private review writing failed; details withheld.");
        }
    }

    private async Task<List<(string Name, string? ETag)>> SelectDocumentsAsync(
        BlobContainerClient container,
        int sourceIndex,
        bool semanticExpectations,
        CancellationToken cancellationToken
    )
    {
        var name = _configuration[$"DocumentQuality:Sources:{sourceIndex}:BlobName"];
        if (!string.IsNullOrWhiteSpace(name))
        {
            Assert.True(name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                && !name.Replace('\\', '/').Split('/').Any(segment => segment is "." or ".."),
                "A PDF blob name without relative path segments is required.");
            var properties = await container.GetBlobClient(name).GetPropertiesAsync(cancellationToken: cancellationToken);
            return [(name, properties.Value.ETag.ToString())];
        }
        Assert.False(semanticExpectations, "Semantic acceptance requires an explicit BlobName for each source.");
        var selected = new List<(string Name, string? ETag)>();
        var inspected = 0;
        await foreach (var page in container.GetBlobsAsync(cancellationToken: cancellationToken).AsPages(pageSizeHint: 10))
        {
            foreach (var item in page.Values)
            {
                if (item.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    selected.Add((item.Name, item.Properties.ETag?.ToString()));
                if (++inspected >= 50 || selected.Count >= 2)
                    break;
            }
            if (inspected >= 50 || selected.Count >= 2)
                break;
        }
        Assert.True(selected.Count > 0, "No PDFs found within the bounded 50-blob discovery; configure an explicit BlobName.");
        return selected;
    }

    private async Task<(ReadOnlyMemory<byte> Original, List<DocumentQualityReviewWriter.Page> Pages)> CompareLayoutAsync(
        DocumentBlobDownloadService download,
        DocumentIngestionClaim claim,
        PdfPreparationResult prepared,
        int maximumBytes,
        CancellationToken cancellationToken
    )
    {
        // Preparation has bounded and accepted this exact ETag; a changed source fails the second conditional read.
        await using var stream = await download.OpenReadAsync(claim, cancellationToken);
        var payload = new byte[maximumBytes + 1];
        var length = 0;
        while (length < payload.Length)
        {
            var read = await stream.ReadAsync(payload.AsMemory(length), cancellationToken);
            if (read == 0)
                break;
            length += read;
        }
        Assert.True(length <= maximumBytes, "The comparison PDF exceeds the configured byte limit.");
        Assert.True(string.Equals(Convert.ToHexString(SHA256.HashData(payload.AsSpan(0, length))),
            prepared.SourceSha256, StringComparison.Ordinal), "Comparison bytes differ from the prepared source; values withheld.");
        using var bytes = new MemoryStream(payload, 0, length, writable: false);
        using var pdf = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false, SkipMissingFonts = false });
        Assert.Equal(prepared.PageCount, pdf.NumberOfPages);
        var tokenizer = TiktokenTokenizer.CreateForEncoding(prepared.Profile.TokenizerEncoding);
        var pages = new List<DocumentQualityReviewWriter.Page>();
        var layouts = new List<DocumentLayoutReview.Snapshot>();
        var different = 0;
        for (var number = 1; number <= pdf.NumberOfPages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pdf.GetPage(number);
            var text = ContentOrderTextExtractor.GetText(page).ReplaceLineEndings("\n");
            var spatial = DocumentLayoutQuality.SpatialText(page);
            if (!string.Equals(DocumentLayoutQuality.Normalize(text), DocumentLayoutQuality.Normalize(spatial), StringComparison.Ordinal))
                different++;
            pages.Add(new(number, text, spatial));
            layouts.Add(DocumentLayoutReview.Capture(page));
            Assert.Equal(string.IsNullOrWhiteSpace(text), prepared.PagesWithoutText.Contains(number));
            var covered = 0;
            foreach (var chunk in prepared.Chunks.Where(chunk => chunk.PageNumber == page.Number))
            {
                Assert.InRange(chunk.StartOffset, 0, covered);
                Assert.InRange(chunk.EndOffset, covered + 1, text.Length);
                Assert.True(text.AsSpan(chunk.StartOffset, chunk.EndOffset - chunk.StartOffset).SequenceEqual(chunk.Text.AsSpan()),
                    "A chunk differs from its recorded extracted-text span; text withheld.");
                Assert.Equal(tokenizer.CountTokens(chunk.Text, considerNormalization: false), chunk.TokenCount);
                covered = chunk.EndOffset;
            }
            Assert.True(string.IsNullOrWhiteSpace(text) || covered == text.Length, "Chunk coverage is incomplete; text withheld.");
        }
        _output.WriteLine($"Content-order/spatial text agrees on every page: {different == 0}. Disagreement is not a quality score.");
        var candidates = DocumentLayoutReview.Render(layouts, cancellationToken);
        for (var index = 0; index < pages.Count; index++)
        {
            Assert.InRange(candidates[index].RetainedWords, 0, candidates[index].SourceWords);
            pages[index] = pages[index] with { Layout = candidates[index] };
        }
        return (payload.AsMemory(0, length), pages);
    }

    private void AssertQualityExpectations(PdfPreparationResult result, int sourceIndex, List<string> pages)
    {
        var settings = _configuration.GetSection($"DocumentQuality:Sources:{sourceIndex}");
        var hash = settings["SourceSha256"];
        Assert.True(hash?.Length == 64 && string.Equals(hash, result.SourceSha256, StringComparison.OrdinalIgnoreCase),
            "The source differs from the independently reviewed PDF hash, or its hash is missing; values withheld.");
        Assert.True(settings.GetValue<int>("ExpectedPageCount") == result.PageCount,
            "Page count differs from the independently reviewed expectation; values withheld.");
        var expected = settings.GetSection("RequiredPassages").Get<string[]>() ?? [];
        var ordered = settings.GetSection("OrderedPassages").Get<string[]>() ?? [];
        Assert.True(expected.Length > 0 && ordered.Length >= 2, "Specify required passages and at least two independent reading-order anchors.");
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.False(string.IsNullOrWhiteSpace(expected[index]), "Quality expectations must not be blank.");
            Assert.True(result.Chunks.Any(chunk => DocumentLayoutQuality.Normalize(chunk.Text)
                .Contains(DocumentLayoutQuality.Normalize(expected[index]), StringComparison.Ordinal)),
                $"Source {sourceIndex + 1}, expected passage {index + 1} was not preserved within a chunk; text withheld.");
        }
        Assert.All(ordered, anchor => Assert.False(string.IsNullOrWhiteSpace(anchor), "Reading-order anchors must not be blank."));
        Assert.True(DocumentLayoutQuality.InOrder(string.Join("\n", pages), ordered),
            $"Source {sourceIndex + 1} violates the independently specified reading order; text withheld.");
    }
}
#endif
