#if DEBUG
using System.Security.Cryptography;

namespace CasCap.Tests.Unit;

/// <summary>Verifies private review encoding, source fidelity, navigation and write-once output with synthetic content.</summary>
[Trait("Category", "DocumentQuality")]
public sealed class DocumentQualityReviewWriterTests
{
    [Fact]
    public async Task PrivateReviewBundle()
    {
        var directory = CreateDirectory();
        var cancellationToken = TestContext.Current.CancellationToken;
        var original = DocumentPreparationTestData.ReferenceLines("SYNTHETIC", "First passage.", "Second passage.");
        const string text = "<script>alert('synthetic')</script>\n```\n![remote](https://example.com/image)\n2.1 SYNTHETIC HEADING\nBody text.";
        var prepared = CreatePreparation(original, text);
        try
        {
            await DocumentQualityReviewWriter.WriteSourceAsync(directory, "source-1", original,
                [new(1, text, "Exploratory first page."), new(2, "Second page.", "Exploratory second page.")],
                prepared, cancellationToken);
            await DocumentQualityReviewWriter.WriteIndexAsync(directory, ["source-1"], cancellationToken);
            Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(directory, "source-1.pdf"), cancellationToken));
            var html = await File.ReadAllTextAsync(Path.Combine(directory, "source-1.html"), cancellationToken);
            Assert.Contains("&lt;script&gt;", html);
            Assert.DoesNotContain("<script>alert", html);
            Assert.Contains("default-src 'none'", html);
            Assert.DoesNotContain("src=\"https://", html);
            Assert.Contains("source-1.pdf", html);
            Assert.Contains("<option value=\"2\">2</option>", html);
            Assert.Contains("data-page=\"2\"", html);
            Assert.Contains("#page=", html);
            Assert.Contains("showPage(); showView();", html);
            Assert.Contains("<option value=\"content\" selected>", html);
            Assert.DoesNotContain("<option value=\"sections\" selected>", html);
            Assert.Contains("Original baseline Markdown</a>", html);
            Assert.DoesNotContain("Refined section Markdown", html);
            Assert.Contains("Chunk 0: tokens=1", html);
            Assert.Contains(prepared.Profile.Id, html);
            Assert.Contains(prepared.SourceSha256!, html);
            var markdown = await File.ReadAllTextAsync(Path.Combine(directory, "source-1.md"), cancellationToken);
            Assert.StartsWith("# Private PDF baseline review", markdown);
            var spaced = DocumentQualityReviewWriter.AddHeadingSpacing(text);
            Assert.Contains($"````text\n{spaced}\n````", markdown);
            Assert.Contains($"````text\n{text}\n````", markdown);
            Assert.Contains("image)\n\n2.1 SYNTHETIC HEADING\n\nBody text.", spaced);
            Assert.Contains("2.1 SYNTHETIC HEADING\n\nBody text.", html);
            var index = await File.ReadAllTextAsync(Path.Combine(directory, "index.html"), cancellationToken);
            Assert.Contains("href=\"source-1.html\"", index);
            Assert.Contains("Do not commit, publish or upload", index);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Theory]
    [InlineData("Previous text.\n2.1 SYNTHETIC HEADING\nBody text.", "Previous text.\n\n2.1 SYNTHETIC HEADING\n\nBody text.")]
    [InlineData("2 INTRODUCTION\n2.1 DETAILS\nBody.", "2 INTRODUCTION\n\n2.1 DETAILS\n\nBody.")]
    [InlineData("Previous.\n\n3.2 DETAILS\n\nBody.", "Previous.\n\n3.2 DETAILS\n\nBody.")]
    [InlineData("3.2 DETAILS\r\nBody.", "3.2 DETAILS\n\nBody.")]
    [InlineData("Publication 0000\n4\n5\n3 Use the button.\n[8] Menu display", "Publication 0000\n4\n5\n3 Use the button.\n[8] Menu display")]
    [InlineData("", "")]
    public void HeadingSpacing(string original, string expected)
    {
        var spaced = DocumentQualityReviewWriter.AddHeadingSpacing(original);
        Assert.Equal(expected, spaced);
        Assert.Equal(spaced, DocumentQualityReviewWriter.AddHeadingSpacing(spaced));
        Assert.Equal(original.ReplaceLineEndings("\n").Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)),
            spaced.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/file")]
    [InlineData("nested\\file")]
    [InlineData("<markup>")]
    public async Task UnsafeFilename(string stem)
    {
        var directory = CreateDirectory();
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => DocumentQualityReviewWriter.WriteSourceAsync(
                directory, stem, new byte[] { 1 }, [], CreatePreparation([1], "synthetic"), TestContext.Current.CancellationToken));
            Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ExistingOutputIsNotOverwritten()
    {
        var directory = CreateDirectory();
        var cancellationToken = TestContext.Current.CancellationToken;
        byte[] original = [1, 2, 3];
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, "source-1.pdf"), original, cancellationToken);
            await Assert.ThrowsAsync<IOException>(() => DocumentQualityReviewWriter.WriteSourceAsync(directory, "source-1",
                new byte[] { 4 }, [], CreatePreparation([4], "synthetic"), cancellationToken));
            Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(directory, "source-1.pdf"), cancellationToken));
            Assert.Single(Directory.EnumerateFileSystemEntries(directory));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static PdfPreparationResult CreatePreparation(byte[] original, string text) => new()
    {
        Status = PdfPreparationStatus.Prepared,
        SourceSha256 = Convert.ToHexString(SHA256.HashData(original)),
        Profile = new DocumentPreparationProfile("SyntheticBaseline", "synthetic", "1", "1", 512, 64),
        PageCount = 2,
        Chunks = [new(0, 1, 0, text.Length, 1, text)],
    };

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        foreach (var name in new[] { "source-1.pdf", "source-1.html", "source-1.md", "index.html" })
            File.Delete(Path.Combine(directory, name));
        Directory.Delete(directory);
    }
}
#endif
