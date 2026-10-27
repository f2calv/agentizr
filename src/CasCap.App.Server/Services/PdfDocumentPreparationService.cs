using System.Security.Cryptography;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace CasCap.Services;

/// <summary>Prepares bounded PDF streams locally; owns no Blob access, durable outcome or index activation.</summary>
internal sealed class PdfDocumentPreparationService(
    IOptions<DocumentPreparationConfig> config,
    DocumentChunkingService documentChunkingSvc)
{
    /// <summary>Prepares from the stream's current position using startup-default chunking; leaves the stream open.</summary>
    public Task<PdfPreparationResult> PrepareAsync(Stream source, CancellationToken cancellationToken) =>
        PrepareAsync(source, config.Value.Chunking, cancellationToken);

    /// <summary>Uses explicit per-call chunking settings, allowing independently tuned application collections.</summary>
    public async Task<PdfPreparationResult> PrepareAsync(
        Stream source,
        DocumentChunkingConfig chunking,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("A readable PDF stream is required.", nameof(source));
        var profile = documentChunkingSvc.GetProfile(chunking);
        cancellationToken.ThrowIfCancellationRequested();
        using var payload = new MemoryStream();
        await ReadBoundedAsync(source, payload, cancellationToken);
        if (payload.Length > config.Value.MaximumPdfBytes)
            return Rejected(PdfPreparationStatus.ByteLimitExceeded, profile);
        if (payload.Length < 5 || !payload.GetBuffer().AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            return Rejected(PdfPreparationStatus.InvalidPdf, profile);
        payload.Position = 0;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var pdf = PdfDocument.Open(payload, new ParsingOptions { UseLenientParsing = false, SkipMissingFonts = false });
            cancellationToken.ThrowIfCancellationRequested();
            if (pdf.IsEncrypted)
                return Rejected(PdfPreparationStatus.EncryptedPdf, profile);
            if (pdf.NumberOfPages > config.Value.MaximumPages)
                return Rejected(PdfPreparationStatus.PageLimitExceeded, profile, pdf.NumberOfPages);
            var sourceSha256 = Convert.ToHexString(SHA256.HashData(payload.GetBuffer().AsSpan(0, (int)payload.Length)));
            return Extract(pdf, profile, chunking, sourceSha256, cancellationToken);
        }
        catch (PdfDocumentEncryptedException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Rejected(PdfPreparationStatus.EncryptedPdf, profile);
        }
        catch (PdfDocumentFormatException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Rejected(PdfPreparationStatus.InvalidPdf, profile);
        }
    }

    private async Task ReadBoundedAsync(Stream source, MemoryStream payload, CancellationToken cancellationToken)
    {
        var buffer = new byte[65_536];
        while (payload.Length <= config.Value.MaximumPdfBytes)
        {
            var capacity = (int)Math.Min(buffer.Length, config.Value.MaximumPdfBytes + 1L - payload.Length);
            var read = await source.ReadAsync(buffer.AsMemory(0, capacity), cancellationToken);
            if (read == 0)
                break;
            await payload.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private PdfPreparationResult Extract(
        PdfDocument pdf,
        DocumentPreparationProfile profile,
        DocumentChunkingConfig chunking,
        string sourceSha256,
        CancellationToken cancellationToken
    )
    {
        var chunks = new List<DocumentTextChunk>();
        var pagesWithoutText = new List<int>();
        var characters = 0;
        for (var pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = ContentOrderTextExtractor.GetText(pdf.GetPage(pageNumber)).ReplaceLineEndings("\n");
            cancellationToken.ThrowIfCancellationRequested();
            if (text.Length > config.Value.MaximumExtractedCharacters - characters)
                return Rejected(PdfPreparationStatus.TextLimitExceeded, profile, pdf.NumberOfPages);
            characters += text.Length;
            if (string.IsNullOrWhiteSpace(text))
            {
                pagesWithoutText.Add(pageNumber);
                continue;
            }
            foreach (var chunk in documentChunkingSvc.ChunkPage(text, pageNumber, chunking, cancellationToken))
            {
                if (chunks.Count >= config.Value.MaximumChunks)
                    return Rejected(PdfPreparationStatus.ChunkLimitExceeded, profile, pdf.NumberOfPages);
                chunks.Add(chunk with { Ordinal = chunks.Count });
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new PdfPreparationResult
        {
            Status = chunks.Count == 0 ? PdfPreparationStatus.NoExtractableText : PdfPreparationStatus.Prepared,
            Profile = profile,
            SourceSha256 = sourceSha256,
            PageCount = pdf.NumberOfPages,
            PagesWithoutText = pagesWithoutText.ToArray(),
            Chunks = chunks.ToArray(),
        };
    }

    private static PdfPreparationResult Rejected(
        PdfPreparationStatus status,
        DocumentPreparationProfile profile,
        int pageCount = 0
    ) => new() { Status = status, Profile = profile, PageCount = pageCount };
}
