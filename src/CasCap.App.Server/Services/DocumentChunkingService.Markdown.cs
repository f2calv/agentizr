using CasCap.Constants;
using Markdig;
using Markdig.Syntax;
using Microsoft.ML.Tokenizers;
using System.Text;
using System.Text.Json;

namespace CasCap.Services;

internal sealed partial class DocumentChunkingService
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();

    /// <summary>Captures the heading strategy and parser/input-budget settings without changing page-local profile identities.</summary>
    public DocumentMarkdownChunkingProfile GetMarkdownProfile(DocumentMarkdownChunkingConfig settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validator.ValidateObject(settings, new ValidationContext(settings), validateAllProperties: true);
        return new(DocumentPreparationFormats.MarkdownHeadings, 3,
            typeof(Markdown).Assembly.GetName().Version!.ToString(), "commonmark-pipe-tables-v1",
            DocumentPreparationFormats.Cl100kBase, typeof(TiktokenTokenizer).Assembly.GetName().Version!.ToString(),
            settings.TargetTokensPerChunk ?? settings.MaximumTokensPerChunk,
            settings.MaximumTokensPerChunk, settings.OverlapTokens, "source-heading-path-v1");
    }

    /// <summary>Builds review-only chunks from page Markdown, retaining source spans and resetting hierarchy across missing pages.</summary>
    public DocumentMarkdownChunkingResult ChunkMarkdown(
        IReadOnlyList<DocumentMarkdownPage> pages,
        DocumentMarkdownChunkingConfig settings,
        DocumentPreparationConfig limits,
        CancellationToken cancellationToken
    ) => ChunkMarkdown(pages, settings, limits, null, cancellationToken);

    /// <summary>Applies explicit role and structural decisions without rewriting source text or changing its offsets.</summary>
    public DocumentMarkdownChunkingResult ChunkMarkdown(
        IReadOnlyList<DocumentMarkdownPage> pages,
        DocumentMarkdownChunkingConfig settings,
        DocumentPreparationConfig limits,
        DocumentMarkdownSourceReview? review,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();
        var profile = GetMarkdownProfile(settings);
        Validator.ValidateObject(limits, new ValidationContext(limits), validateAllProperties: true);
        if (pages.Count > limits.MaximumPages)
            throw new InvalidDataException("Markdown preparation exceeds the page limit.");
        long characters = 0;
        var previousPage = 0;
        foreach (var page in pages)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(page.Markdown);
            if (page.PageNumber <= previousPage || page.PageNumber > limits.MaximumPages)
                throw new ArgumentException("Markdown pages must have increasing original physical numbers within the source limit.", nameof(pages));
            previousPage = page.PageNumber;
            characters += page.Markdown.Length;
            if (characters > limits.MaximumExtractedCharacters)
                throw new InvalidDataException("Markdown preparation exceeds the character limit.");
        }

        var digest = DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(pages));
        review = ValidateMarkdownReview(review, pages, digest);
        var matched = new HashSet<(int PageNumber, int StartOffset, int EndOffset)>();
        var chunks = new List<DocumentMarkdownChunk>();
        var blockOrdinal = 0;
        var first = 0;
        while (first < pages.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var last = first + 1;
            while (last < pages.Count && pages[last].PageNumber == pages[last - 1].PageNumber + 1)
                last++;
            var source = ReadMarkdown(pages.Skip(first).Take(last - first), review, matched, ref blockOrdinal);
            lock (_tokenizer.Value)
                PackMarkdown(source, profile, limits.MaximumChunks, chunks, cancellationToken);
            first = last;
        }
        if (review is not null && matched.Count != review.Exclusions.Count + review.Headings.Count + review.LayoutTables.Count)
            throw new InvalidDataException("A reviewed decision does not identify a supported complete parsed source block.");
        cancellationToken.ThrowIfCancellationRequested();
        return new(profile, digest, chunks.ToArray(), review);
    }

    private static MarkdownSource ReadMarkdown(
        IEnumerable<DocumentMarkdownPage> pages,
        DocumentMarkdownSourceReview? review,
        HashSet<(int PageNumber, int StartOffset, int EndOffset)> matched,
        ref int blockOrdinal
    )
    {
        var text = new StringBuilder();
        var segments = new List<MarkdownPageSegment>();
        foreach (var page in pages)
        {
            if (segments.Count != 0)
                text.Append("\n\n");
            var start = text.Length;
            text.Append(page.Markdown);
            segments.Add(new(page.PageNumber, start, text.Length));
        }
        var source = new MarkdownSource(text.ToString(), segments, []);
        var headings = new List<DocumentMarkdownHeading>();
        foreach (var block in Markdown.Parse(source.Text, MarkdownPipeline))
        {
            var excluded = ExcludeMarkdownBlock(source, block, review, matched);
            var correction = ReviewedMarkdownHeading(source, block, review, matched);
            if (block is HeadingBlock || correction is not null)
            {
                var ordinal = blockOrdinal++;
                if (excluded)
                    continue;
                var level = correction?.Level ?? ((HeadingBlock)block).Level;
                var markdown = block is HeadingBlock heading
                    ? HeadingMarkdown(source.Text, heading)
                    : source.Text[block.Span.Start..(block.Span.End + 1)];
                if (correction is not null)
                    markdown = new string('#', level) + " " + (block is HeadingBlock original
                        ? markdown[(original.Level + 1)..] : markdown);
                headings.RemoveAll(item => item.Level >= level);
                headings.Add(new(level, markdown, MapMarkdown(source, block.Span.Start, block.Span.End + 1, ordinal)));
                continue;
            }
            if (block is ListBlock list)
            {
                foreach (var item in list)
                {
                    var ordinal = blockOrdinal++;
                    if (!excluded)
                        source.Units.Add(new(item.Span.Start, item.Span.End + 1, ordinal, headings.ToArray(), false));
                }
            }
            else
            {
                var ordinal = blockOrdinal++;
                if (!excluded)
                    source.Units.Add(ReviewedMarkdownTable(source, block, review, matched, ordinal, headings.ToArray())
                        ?? new(block.Span.Start, block.Span.End + 1, ordinal, headings.ToArray(), block is ParagraphBlock));
            }
        }
        return source;
    }

    private void PackMarkdown(
        MarkdownSource source,
        DocumentMarkdownChunkingProfile profile,
        int maximumChunks,
        List<DocumentMarkdownChunk> chunks,
        CancellationToken cancellationToken
    )
    {
        var pending = new List<MarkdownPiece>();
        DocumentMarkdownHeading[] path = [];
        foreach (var unit in source.Units)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!path.SequenceEqual(unit.Headings))
            {
                Flush();
                path = unit.Headings;
            }
            var piece = new MarkdownPiece(unit.Start, unit.End, unit.Ordinal, false, unit.Projection);
            var text = unit.Projection?.Text ?? source.Text[unit.Start..unit.End];
            var prefix = Prefix(path);
            if (Count(prefix + text) <= profile.MaximumTokensPerChunk)
            {
                if (pending.Count != 0 && Count(prefix + Body(pending.Append(piece))) > profile.TargetTokensPerChunk)
                    Flush();
                pending.Add(piece);
                if (Count(prefix + Body(pending)) >= profile.TargetTokensPerChunk)
                    Flush();
                continue;
            }
            Flush();
            if (!unit.CanSplit)
                // TODO: Add validated table/header and warning/entry subdivision before relaxing this rejection.
                throw new InvalidDataException("A structured Markdown unit exceeds its heading-inclusive budget; review or semantic subdivision is required.");
            Split(unit, prefix);
        }
        Flush();

        void Flush()
        {
            if (pending.Count == 0)
                return;
            cancellationToken.ThrowIfCancellationRequested();
            var body = Body(pending);
            var input = Prefix(path) + body;
            if (chunks.Count >= maximumChunks || Count(input) > profile.MaximumTokensPerChunk)
                throw new InvalidDataException("Markdown output exceeds its chunk or complete-input token limit.");
            chunks.Add(new(chunks.Count, path, body, input, Count(input),
                pending.SelectMany(item => item.Projection?.Spans ?? MapMarkdown(source, item.Start, item.End, item.Ordinal)).ToArray(),
                pending.Any(item => item.Fragmented)));
            pending.Clear();
        }

        void Split(MarkdownUnit unit, string prefix)
        {
            var reserved = Count(prefix);
            if (reserved >= profile.MaximumTokensPerChunk)
                throw new InvalidDataException("The complete heading path leaves no body token budget.");
            var maximumBodyBudget = profile.MaximumTokensPerChunk - reserved;
            var budget = profile.TargetTokensPerChunk > reserved
                ? profile.TargetTokensPerChunk - reserved : maximumBodyBudget;
            var start = unit.Start;
            var covered = start;
            while (start < unit.End)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = _tokenizer.Value.GetIndexByTokenCount(source.Text.AsSpan(start, unit.End - start), budget,
                    out _, out _, considerNormalization: false);
                var end = BeforeScalarBoundary(source.Text, start + length);
                if (end <= start && budget < maximumBodyBudget)
                {
                    length = _tokenizer.Value.GetIndexByTokenCount(source.Text.AsSpan(start, unit.End - start), maximumBodyBudget,
                        out _, out _, considerNormalization: false);
                    end = BeforeScalarBoundary(source.Text, start + length);
                }
                while (end > start && Count(prefix + source.Text[start..end]) > profile.MaximumTokensPerChunk)
                    end = BeforeScalarBoundary(source.Text, end - 1);
                if (end <= start)
                    throw new InvalidDataException("The heading-inclusive token budget cannot fit the next Unicode scalar.");
                if (end < unit.End)
                    end = SentenceBoundary(source.Text, start, end);
                if (end <= covered)
                {
                    start = covered;
                    continue;
                }
                pending.Add(new(start, end, unit.Ordinal, true));
                Flush();
                covered = end;
                start = end == unit.End ? end : NextStart(source.Text[start..end], start, end, profile.OverlapTokens);
            }
        }

        string Body(IEnumerable<MarkdownPiece> pieces) =>
            string.Join("\n\n", pieces.Select(item => item.Projection?.Text ?? source.Text[item.Start..item.End]));
    }

    private int Count(string text) => _tokenizer.Value.CountTokens(text, considerNormalization: false);

    private static string Prefix(IEnumerable<DocumentMarkdownHeading> path)
    {
        var headings = string.Join('\n', path.Select(heading => heading.Markdown));
        return headings.Length == 0 ? string.Empty : headings + "\n\n";
    }

    private static string HeadingMarkdown(string text, HeadingBlock heading)
    {
        var content = text[heading.Span.Start..(heading.Span.End + 1)].Trim();
        if (heading.IsSetext)
            content = content[..content.LastIndexOf('\n')].TrimEnd();
        else
        {
            content = content[heading.Level..].Trim();
            var closing = content.Length;
            while (closing > 0 && content[closing - 1] == '#')
                closing--;
            if (closing == 0 || char.IsWhiteSpace(content[closing - 1]))
                content = content[..closing].TrimEnd();
        }
        return $"{new string('#', heading.Level)} {content.ReplaceLineEndings(" ")}";
    }

    private static int SentenceBoundary(string text, int start, int end)
    {
        for (var index = end - 1; index > start; index--)
            if (text[index] is '.' or '!' or '?' && char.IsWhiteSpace(text[index + 1]))
                return index + 1;
        return end;
    }

    private static DocumentMarkdownSourceSpan[] MapMarkdown(MarkdownSource source, int start, int end, int ordinal) =>
        source.Pages.Where(page => page.Start < end && page.End > start)
            .Select(page => new DocumentMarkdownSourceSpan(page.PageNumber,
                Math.Max(start, page.Start) - page.Start, Math.Min(end, page.End) - page.Start, ordinal)).ToArray();

    private sealed record MarkdownSource(string Text, List<MarkdownPageSegment> Pages, List<MarkdownUnit> Units);
    private sealed record MarkdownPageSegment(int PageNumber, int Start, int End);
    private sealed record MarkdownProjection(string Text, DocumentMarkdownSourceSpan[] Spans);
    private sealed record MarkdownUnit(int Start, int End, int Ordinal, DocumentMarkdownHeading[] Headings, bool CanSplit,
        MarkdownProjection? Projection = null);
    private sealed record MarkdownPiece(int Start, int End, int Ordinal, bool Fragmented, MarkdownProjection? Projection = null);
}
