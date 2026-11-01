using CasCap.Constants;
using Markdig.Syntax;

namespace CasCap.Services;

internal sealed partial class DocumentChunkingService
{
    private static DocumentMarkdownSourceReview? ValidateMarkdownReview(
        DocumentMarkdownSourceReview? review,
        IReadOnlyList<DocumentMarkdownPage> pages,
        string digest
    )
    {
        if (review is null)
            return null;
        if (review.Version != 2 || string.IsNullOrWhiteSpace(review.ReviewId) || review.ReviewId.Length > 128
            || review.MarkdownSha256 != digest || review.Exclusions is null || review.Headings is null || review.LayoutTables is null
            || (long)review.Exclusions.Count + review.Headings.Count + review.LayoutTables.Count > 5_000)
            throw new InvalidDataException("Source review version, identity, digest or annotation limit is invalid.");
        var exclusions = review.Exclusions.ToArray();
        foreach (var exclusion in exclusions)
        {
            if (exclusion is null || exclusion.Role is not (DocumentMarkdownSourceRoles.RunningHeader or DocumentMarkdownSourceRoles.RunningFooter
                    or DocumentMarkdownSourceRoles.Navigation))
                throw new InvalidDataException("A source exclusion requires a supported role and bounded review reason.");
            ValidateMarkdownReviewRange(pages, exclusion.PageNumber, exclusion.StartOffset, exclusion.EndOffset, exclusion.Reason);
        }
        var headings = review.Headings.ToArray();
        foreach (var heading in headings)
        {
            if (heading is null || heading.Level is < 1 or > 6)
                throw new InvalidDataException("A reviewed heading requires a supported level.");
            ValidateMarkdownReviewRange(pages, heading.PageNumber, heading.StartOffset, heading.EndOffset, heading.Reason);
        }
        var tables = review.LayoutTables.ToArray();
        foreach (var table in tables)
        {
            if (table is null || table.ReadingOrder != DocumentMarkdownReadingOrders.ColumnMajor)
                throw new InvalidDataException("A reviewed layout table requires an explicit supported reading order.");
            ValidateMarkdownReviewRange(pages, table.PageNumber, table.StartOffset, table.EndOffset, table.Reason);
        }
        var ranges = exclusions.Select(item => (item.PageNumber, item.StartOffset, item.EndOffset))
            .Concat(headings.Select(item => (item.PageNumber, item.StartOffset, item.EndOffset)))
            .Concat(tables.Select(item => (item.PageNumber, item.StartOffset, item.EndOffset)))
            .OrderBy(item => item.PageNumber).ThenBy(item => item.StartOffset).ToArray();
        for (var index = 1; index < ranges.Length; index++)
            if (ranges[index].PageNumber == ranges[index - 1].PageNumber
                && ranges[index].StartOffset < ranges[index - 1].EndOffset)
                throw new InvalidDataException("Reviewed source decisions must not overlap or repeat.");
        exclusions = exclusions.OrderBy(item => item.PageNumber).ThenBy(item => item.StartOffset).ToArray();
        return review with
        {
            Exclusions = exclusions,
            Headings = headings.OrderBy(item => item.PageNumber).ThenBy(item => item.StartOffset).ToArray(),
            LayoutTables = tables.OrderBy(item => item.PageNumber).ThenBy(item => item.StartOffset).ToArray(),
        };
    }

    private static void ValidateMarkdownReviewRange(
        IReadOnlyList<DocumentMarkdownPage> pages, int pageNumber, int start, int end, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1_024)
            throw new InvalidDataException("A source decision requires a bounded review reason.");
        var page = pages.FirstOrDefault(page => page.PageNumber == pageNumber);
        if (page is null || start < 0 || end <= start || end > page.Markdown.Length
            || char.IsLowSurrogate(page.Markdown[start]) || char.IsHighSurrogate(page.Markdown[end - 1]))
            throw new InvalidDataException("A source decision has invalid page, offset or Unicode scalar boundaries.");
    }

    private static bool ExcludeMarkdownBlock(
        MarkdownSource source,
        Block block,
        DocumentMarkdownSourceReview? review,
        HashSet<(int PageNumber, int StartOffset, int EndOffset)> matched
    )
    {
        if (review is null || review.Exclusions.Count == 0)
            return false;
        var spans = MapMarkdown(source, block.Span.Start, block.Span.End + 1, 0);
        var annotations = review.Exclusions.Where(exclusion => spans.Any(span =>
            exclusion.PageNumber == span.PageNumber && exclusion.StartOffset < span.EndOffset
            && exclusion.EndOffset > span.StartOffset)).ToArray();
        if (annotations.Length == 0)
            return false;
        if (annotations.Length != spans.Length || annotations.Select(item => item.Role).Distinct(StringComparer.Ordinal).Count() != 1
            || spans.Any(span => !annotations.Any(exclusion => exclusion.PageNumber == span.PageNumber
                && exclusion.StartOffset == span.StartOffset && exclusion.EndOffset == span.EndOffset)))
            throw new InvalidDataException("A reviewed exclusion must cover an entire parsed block with one source role.");
        foreach (var annotation in annotations)
            matched.Add((annotation.PageNumber, annotation.StartOffset, annotation.EndOffset));
        return true;
    }
}
