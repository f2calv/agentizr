#if DEBUG
using Microsoft.ML.Tokenizers;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Verifies actual token limits, lossless Unicode coverage and captured per-call chunking profiles.</summary>
[Trait("Category", "PdfPreparation")]
public sealed class DocumentChunkingTests
{
    private readonly DocumentChunkingService _chunker = new();
    private readonly TiktokenTokenizer _tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");

    [Theory]
    [InlineData(8, 0)]
    [InlineData(8, 7)]
    [InlineData(16, 4)]
    [InlineData(64, 8)]
    [InlineData(128, 16)]
    [InlineData(512, 64)]
    public void TokenWindows(int maximum, int overlap)
    {
        var text = string.Concat(Enumerable.Repeat(
            "Gr\u00fc\u00dfe! Stra\u00dfe, Wartung, Ger\u00e4te. \uD83D\uDE80 \uD83D\uDE03 e\u0301 \u4F60\u597D.\n", 70));
        var settings = new DocumentChunkingConfig { MaximumTokensPerChunk = maximum, OverlapTokens = overlap };
        var chunks = _chunker.ChunkPage(text, 7, settings, TestContext.Current.CancellationToken).ToArray();
        Assert.NotEmpty(chunks);
        var covered = 0;
        var reconstructed = new StringBuilder();
        for (var i = 0; i < chunks.Length; i++)
        {
            var chunk = chunks[i];
            Assert.Equal(7, chunk.PageNumber);
            Assert.Equal(text[chunk.StartOffset..chunk.EndOffset], chunk.Text);
            Assert.Equal(_tokenizer.CountTokens(chunk.Text, considerNormalization: false), chunk.TokenCount);
            Assert.InRange(chunk.TokenCount, 1, maximum);
            Assert.False(char.IsLowSurrogate(chunk.Text[0]));
            Assert.False(char.IsHighSurrogate(chunk.Text[^1]));
            Assert.DoesNotContain("\uFFFD", chunk.Text, StringComparison.Ordinal);
            Assert.InRange(chunk.StartOffset, 0, covered);
            Assert.True(chunk.EndOffset > covered, "Every emitted window must extend coverage.");
            if (i > 0)
            {
                Assert.True(chunk.StartOffset > chunks[i - 1].StartOffset);
                Assert.InRange(_tokenizer.CountTokens(text.AsSpan(chunk.StartOffset, covered - chunk.StartOffset),
                    considerNormalization: false), 0, overlap);
            }
            reconstructed.Append(text.AsSpan(covered, chunk.EndOffset - covered));
            covered = chunk.EndOffset;
        }
        Assert.Equal(text.Length, covered);
        Assert.Equal(text, reconstructed.ToString());
        if (overlap == 0)
            Assert.Equal(text, string.Concat(chunks.Select(chunk => chunk.Text)));
    }

    [Fact]
    public void ShortPageHasNoRedundantOverlapChunk()
    {
        var chunks = _chunker.ChunkPage("Short page.", 1, new(), TestContext.Current.CancellationToken).ToArray();
        Assert.Equal("Short page.", Assert.Single(chunks).Text);
    }

    [Fact]
    public void PerCallProfilesAreCaptured()
    {
        var small = new DocumentChunkingConfig { MaximumTokensPerChunk = 32, OverlapTokens = 4 };
        var large = new DocumentChunkingConfig { MaximumTokensPerChunk = 256, OverlapTokens = 32 };
        var first = _chunker.GetProfile(small);
        var second = _chunker.GetProfile(large);
        Assert.Equal(first.Id, _chunker.GetProfile(small).Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Id, _chunker.GetProfile(small with { OverlapTokens = 0 }).Id);
        Assert.Equal("cl100k_base", first.TokenizerEncoding);
        Assert.False(string.IsNullOrWhiteSpace(first.ExtractorVersion));
        Assert.False(string.IsNullOrWhiteSpace(first.TokenizerVersion));
        var roundTrip = Assert.IsType<DocumentPreparationProfile>(
            JsonSerializer.Deserialize<DocumentPreparationProfile>(JsonSerializer.Serialize(first)));
        Assert.Equal(first, roundTrip);
        Assert.Equal(first.Id, roundTrip.Id);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 0)]
    [InlineData(8193, 0)]
    [InlineData(16, -1)]
    [InlineData(16, 16)]
    [InlineData(16, 17)]
    public void InvalidProfile(int maximum, int overlap) =>
        Assert.Throws<ValidationException>(() => _chunker.GetProfile(
            new DocumentChunkingConfig { MaximumTokensPerChunk = maximum, OverlapTokens = overlap }));

    [Fact]
    public void CancellationBetweenChunks()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var text = string.Concat(Enumerable.Repeat("Long text with many words. ", 100));
        using var chunks = _chunker.ChunkPage(text, 1,
            new DocumentChunkingConfig { MaximumTokensPerChunk = 16, OverlapTokens = 4 }, cancellation.Token).GetEnumerator();
        Assert.True(chunks.MoveNext());
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => chunks.MoveNext());
    }
}
#endif
