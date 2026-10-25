using CasCap.Constants;
using Microsoft.ML.Tokenizers;
using UglyToad.PdfPig;

namespace CasCap.Services;

/// <summary>Builds page-local token windows without decoding incomplete token-byte sequences.</summary>
internal sealed class DocumentChunkingService
{
    private readonly Lazy<TiktokenTokenizer> _tokenizer = new(() =>
        TiktokenTokenizer.CreateForEncoding(DocumentPreparationFormats.Cl100kBase));

    /// <summary>Validates per-call settings and captures the libraries and algorithm used by this process.</summary>
    public DocumentPreparationProfile GetProfile(DocumentChunkingConfig settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validator.ValidateObject(settings, new ValidationContext(settings), validateAllProperties: true);
        return new(DocumentPreparationFormats.PageLocalV1, DocumentPreparationFormats.Cl100kBase,
            typeof(PdfDocument).Assembly.GetName().Version!.ToString(),
            typeof(TiktokenTokenizer).Assembly.GetName().Version!.ToString(),
            settings.MaximumTokensPerChunk, settings.OverlapTokens);
    }

    /// <summary>Enumerates complete text coverage; adjacent chunks overlap by at most the configured token budget.</summary>
    public IEnumerable<DocumentTextChunk> ChunkPage(
        string text,
        int pageNumber,
        DocumentChunkingConfig settings,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        var profile = GetProfile(settings);
        var start = 0;
        var coveredEnd = 0;
        while (start < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentTextChunk chunk;
            int next;
            // Serialize tokenizer cache access without retaining a lock across a yielded chunk.
            lock (_tokenizer.Value)
            {
                chunk = CreateChunk(text, pageNumber, start, profile);
                next = chunk.EndOffset == text.Length ? text.Length : NextStart(chunk, profile);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (chunk.EndOffset <= coveredEnd)
            {
                // Retokenizing an overlap can consume the entire budget without covering new text.
                start = coveredEnd;
                continue;
            }
            coveredEnd = chunk.EndOffset;
            yield return chunk;
            start = next;
        }
    }

    private DocumentTextChunk CreateChunk(string text, int pageNumber, int start, DocumentPreparationProfile profile)
    {
        var length = _tokenizer.Value.GetIndexByTokenCount(text.AsSpan(start), profile.MaximumTokensPerChunk,
            out _, out _, considerNormalization: false);
        var end = BeforeScalarBoundary(text, start + length);
        while (end > start && _tokenizer.Value.CountTokens(text.AsSpan(start, end - start),
            considerNormalization: false) > profile.MaximumTokensPerChunk)
            end = BeforeScalarBoundary(text, end - 1);
        if (end <= start)
            throw new InvalidOperationException("The tokenizer could not advance within the validated chunk budget.");
        var chunk = text[start..end];
        return new(0, pageNumber, start, end,
            _tokenizer.Value.CountTokens(chunk, considerNormalization: false), chunk);
    }

    private int NextStart(DocumentTextChunk chunk, DocumentPreparationProfile profile)
    {
        if (profile.OverlapTokens == 0)
            return chunk.EndOffset;
        var overlap = _tokenizer.Value.GetIndexByTokenCountFromEnd(chunk.Text, profile.OverlapTokens,
            out _, out _, considerNormalization: false);
        overlap = AfterScalarBoundary(chunk.Text, overlap);
        while (overlap < chunk.Text.Length && _tokenizer.Value.CountTokens(chunk.Text.AsSpan(overlap),
            considerNormalization: false) > profile.OverlapTokens)
            overlap = AfterScalarBoundary(chunk.Text, overlap + 1);
        return overlap == 0 ? chunk.EndOffset : chunk.StartOffset + overlap;
    }

    private static int BeforeScalarBoundary(string text, int index) =>
        index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index])
            ? index - 1 : index;

    private static int AfterScalarBoundary(string text, int index) =>
        index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index])
            ? index + 1 : index;
}
