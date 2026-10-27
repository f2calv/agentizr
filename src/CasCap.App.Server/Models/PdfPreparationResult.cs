namespace CasCap.Models;

/// <summary>Prepared text or an explicit rejection; rejected results never expose partial chunks.</summary>
internal sealed record PdfPreparationResult
{
    /// <inheritdoc cref="PdfPreparationStatus" path="/summary"/>
    public required PdfPreparationStatus Status { get; init; }

    /// <summary>Gets the captured extraction and chunking profile, including token-budget settings.</summary>
    public required DocumentPreparationProfile Profile { get; init; }

    /// <summary>Gets the SHA-256 digest of the bounded original bytes when extraction reached them.</summary>
    public string? SourceSha256 { get; init; }

    /// <summary>Gets the PDF page count when parsing reached it.</summary>
    public int PageCount { get; init; }

    /// <summary>Gets pages with no extracted text; these may be blank or require OCR.</summary>
    public IReadOnlyList<int> PagesWithoutText { get; init; } = [];

    /// <summary>Gets complete preparation output, or an empty collection for every rejected result.</summary>
    public IReadOnlyList<DocumentTextChunk> Chunks { get; init; } = [];
}
