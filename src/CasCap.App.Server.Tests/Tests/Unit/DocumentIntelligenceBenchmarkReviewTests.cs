#if DEBUG
namespace CasCap.Tests.Unit;

/// <summary>Checks safe private exports and non-overwriting resume behavior using synthetic content only.</summary>
[Trait("Category", "DocumentQuality")]
public sealed class DocumentIntelligenceBenchmarkReviewTests
{
    [Fact]
    public async Task EscapedComparisonAndImmutableExports()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        const string text = "<img src=\"https://example.com/tracker\"> & private synthetic text";
        var local = new Dictionary<int, string> { [2] = text };
        var managed = new DocumentIntelligenceQualityClient.Result(text, local);
        try
        {
            await WriteAsync();
            await WriteAsync();
            var html = await File.ReadAllTextAsync(Path.Combine(directory, "sample.html"), TestContext.Current.CancellationToken);
            Assert.DoesNotContain("<img", html);
            Assert.Contains("&lt;img", html);
            Assert.Contains("sample.pdf#page=2", html);
            Assert.Equal(text, await File.ReadAllTextAsync(Path.Combine(directory, "sample.managed.md"), TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() => DocumentIntelligenceBenchmarkReview.WriteAsync(
                directory, "sample", "%PDF-changed"u8.ToArray(), local, managed, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Task WriteAsync()
        {
            return DocumentIntelligenceBenchmarkReview.WriteAsync(
                directory, "sample", "%PDF-synthetic"u8.ToArray(), local, managed, TestContext.Current.CancellationToken);
        }
    }
}
#endif
