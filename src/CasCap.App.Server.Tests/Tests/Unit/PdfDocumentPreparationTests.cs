#if DEBUG
using System.Text;
using System.Globalization;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CasCap.Tests.Unit;

/// <summary>Uses generated synthetic PDFs only; no licensed documents, cloud storage or external services.</summary>
[Trait("Category", "PdfPreparation")]
public sealed class PdfDocumentPreparationTests
{
    private readonly DocumentChunkingService _chunker = new();

    [Fact]
    public async Task PagesAndGlobalChunkOrdinals()
    {
        using var source = new MemoryStream(Pdf("Device maintenance instructions.", "", "German practice vocabulary."));
        var result = await Create().PrepareAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal(PdfPreparationStatus.Prepared, result.Status);
        Assert.Equal(3, result.PageCount);
        Assert.Equal([2], result.PagesWithoutText);
        Assert.Equal([1, 3], result.Chunks.Select(chunk => chunk.PageNumber));
        Assert.Equal([0, 1], result.Chunks.Select(chunk => chunk.Ordinal));
        Assert.Contains("Device maintenance", result.Chunks[0].Text, StringComparison.Ordinal);
        Assert.Contains("German practice", result.Chunks[1].Text, StringComparison.Ordinal);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task PerCallBudgetsRemainIndependent()
    {
        var pdf = Pdf(string.Join(' ', Enumerable.Repeat("Learning maintenance vocabulary.", 80)));
        using var first = new MemoryStream(pdf);
        using var second = new MemoryStream(pdf);
        var svc = Create();
        var small = await svc.PrepareAsync(first,
            new DocumentChunkingConfig { MaximumTokensPerChunk = 16, OverlapTokens = 4 }, TestContext.Current.CancellationToken);
        var large = await svc.PrepareAsync(second,
            new DocumentChunkingConfig { MaximumTokensPerChunk = 128, OverlapTokens = 16 }, TestContext.Current.CancellationToken);
        Assert.Equal(PdfPreparationStatus.Prepared, small.Status);
        Assert.Equal(PdfPreparationStatus.Prepared, large.Status);
        Assert.True(small.Chunks.Count > large.Chunks.Count);
        Assert.NotEqual(small.Profile.Id, large.Profile.Id);
        Assert.All(small.Chunks, chunk => Assert.InRange(chunk.TokenCount, 1, 16));
        Assert.All(large.Chunks, chunk => Assert.InRange(chunk.TokenCount, 1, 128));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactByteLimit(bool oversized)
    {
        var bytes = Pdf("Boundary test.");
        using var source = new MemoryStream(bytes);
        var result = await Create(new() { MaximumPdfBytes = bytes.Length - (oversized ? 1 : 0) })
            .PrepareAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal(oversized ? PdfPreparationStatus.ByteLimitExceeded : PdfPreparationStatus.Prepared, result.Status);
        if (oversized)
            Assert.Empty(result.Chunks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactPageLimit(bool oversized)
    {
        using var source = new MemoryStream(Pdf("Page one.", "Page two."));
        var result = await Create(new() { MaximumPages = oversized ? 1 : 2 })
            .PrepareAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal(oversized ? PdfPreparationStatus.PageLimitExceeded : PdfPreparationStatus.Prepared, result.Status);
        Assert.Equal(2, result.PageCount);
        if (oversized)
            Assert.Empty(result.Chunks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactTextLimitAcrossPages(bool oversized)
    {
        var bytes = Pdf("First page.", "Second page.");
        using var pdf = PdfDocument.Open(bytes);
        var length = pdf.GetPages().Sum(page => ContentOrderTextExtractor.GetText(page).ReplaceLineEndings("\n").Length);
        using var source = new MemoryStream(bytes);
        var result = await Create(new() { MaximumExtractedCharacters = length - (oversized ? 1 : 0) })
            .PrepareAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal(oversized ? PdfPreparationStatus.TextLimitExceeded : PdfPreparationStatus.Prepared, result.Status);
        if (oversized)
            Assert.Empty(result.Chunks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactChunkLimitRejectsPartialOutput(bool oversized)
    {
        using var source = new MemoryStream(Pdf("First page.", "Second page."));
        var result = await Create(new() { MaximumChunks = oversized ? 1 : 2 })
            .PrepareAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal(oversized ? PdfPreparationStatus.ChunkLimitExceeded : PdfPreparationStatus.Prepared, result.Status);
        Assert.Equal(oversized ? 0 : 2, result.Chunks.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a PDF")]
    [InlineData("%PDF-1.7\nbroken")]
    public async Task InvalidPdf(string input)
    {
        using var source = new MemoryStream(Encoding.ASCII.GetBytes(input));
        var result = await Create().PrepareAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal(PdfPreparationStatus.InvalidPdf, result.Status);
        Assert.Empty(result.Chunks);
    }

    [Fact]
    public async Task NoTextIsExplicit()
    {
        using var source = new MemoryStream(Pdf("", ""));
        var result = await Create().PrepareAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal(PdfPreparationStatus.NoExtractableText, result.Status);
        Assert.Equal([1, 2], result.PagesWithoutText);
        Assert.Empty(result.Chunks);
    }

    [Fact]
    public async Task EncryptedPdfIsExplicit()
    {
        using var source = new MemoryStream(EncryptedPdf());
        var result = await Create().PrepareAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal(PdfPreparationStatus.EncryptedPdf, result.Status);
        Assert.Empty(result.Chunks);
    }

    [Fact]
    public async Task CancellationBeforeRead()
    {
        using var source = new MemoryStream(Pdf("Unused document."));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().PrepareAsync(source, cancellation.Token));
        Assert.Equal(0, source.Position);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task NonSeekableInputIsBoundedAndBorrowed()
    {
        using var source = new ObservedStream(new byte[10_000]);
        var result = await Create(new() { MaximumPdfBytes = 100 })
            .PrepareAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal(PdfPreparationStatus.ByteLimitExceeded, result.Status);
        Assert.Equal(101, source.BytesRead);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task NonSeekablePdf()
    {
        using var source = new ObservedStream(Pdf("Nonseekable source."));
        var result = await Create().PrepareAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal(PdfPreparationStatus.Prepared, result.Status);
        Assert.Contains("Nonseekable source.", Assert.Single(result.Chunks).Text, StringComparison.Ordinal);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task StartsAtCurrentPosition()
    {
        var bytes = Pdf("Selected source position.");
        using var source = new MemoryStream([1, 2, 3, .. bytes]);
        source.Position = 3;
        var result = await Create(new() { MaximumPdfBytes = bytes.Length })
            .PrepareAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal(PdfPreparationStatus.Prepared, result.Status);
        Assert.Contains("Selected source position.", Assert.Single(result.Chunks).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationDuringRead()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var source = new ObservedStream(Pdf("Cancelled source.")) { OnRead = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().PrepareAsync(source, cancellation.Token));
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task StreamFailureIsNotAnInvalidPdf()
    {
        using var source = new ObservedStream([]) { FailRead = true };
        await Assert.ThrowsAsync<IOException>(() => Create().PrepareAsync(source, TestContext.Current.CancellationToken));
        Assert.True(source.CanRead);
    }

    private PdfDocumentPreparationService Create(DocumentPreparationConfig? config = null) =>
        new(Options.Create(config ?? new()), _chunker);

    private static byte[] Pdf(params string[] pages)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            if (text.Length > 0)
                page.AddText(text, 10, new PdfPoint(25, 700), font);
        }
        return builder.Build();
    }

    private static byte[] EncryptedPdf()
    {
        var zeros = new string('0', 64);
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> /Contents 4 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream",
            $"<< /Filter /Standard /V 1 /R 2 /Length 40 /O <{zeros}> /U <{zeros}> /P -4 >>",
        };
        var content = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(content.Length);
            content.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = content.Length;
        content.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            content.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        content.Append(CultureInfo.InvariantCulture,
            $"trailer\n<< /Size 6 /Root 1 0 R /Encrypt 5 0 R /ID [<00000000000000000000000000000000><00000000000000000000000000000000>] >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(content.ToString());
    }

    private sealed class ObservedStream(byte[] bytes) : Stream
    {
        private int _position;
        private bool _disposed;
        public bool FailRead { get; init; }
        public Action? OnRead { get; init; }
        public int BytesRead => _position;
        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailRead)
                throw new IOException("Synthetic source read failure.");
            var length = Math.Min(buffer.Length, bytes.Length - _position);
            bytes.AsMemory(_position, length).CopyTo(buffer);
            _position += length;
            OnRead?.Invoke();
            return ValueTask.FromResult(length);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
#endif
