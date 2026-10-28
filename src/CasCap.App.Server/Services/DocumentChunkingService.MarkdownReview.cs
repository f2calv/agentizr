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
        if (review.Version != 1 || string.IsNullOrWhiteSpace(review.ReviewId) || review.ReviewId.Length > 128
            || review.MarkdownSha256 != digest || review.Exclusions is null || review.Exclusions.Count > 5_000)
            throw new InvalidDataException("Source review version, identity, digest or annotation limit is invalid.");
        var exclusions = review.Exclusions.ToArray();
        foreach (var exclusion in exclusions)
        {
            if (exclusion is null || string.IsNullOrWhiteSpace(exclusion.Reason) || exclusion.Reason.Length > 1_024
                || exclusion.Role is not (DocumentMarkdownSourceRoles.RunningHeader or DocumentMarkdownSourceRoles.RunningFooter
                    or DocumentMarkdownSourceRoles.Navigation))
                throw new InvalidDataException("A source exclusion requires a supported role and bounded review reason.");
            var page = pages.FirstOrDefault(page => page.PageNumber == exclusion.PageNumber);
            if (page is null || exclusion.StartOffset < 0 || exclusion.EndOffset <= exclusion.StartOffset
                || exclusion.EndOffset > page.Markdown.Length
                || char.IsLowSurrogate(page.Markdown[exclusion.StartOffset])
                || char.IsHighSurrogate(page.Markdown[exclusion.EndOffset - 1]))
                throw new InvalidDataException("A source exclusion has invalid page, offset or Unicode scalar boundaries.");
        }
        exclusions = exclusions.OrderBy(item => item.PageNumber).ThenBy(item => item.StartOffset).ToArray();
        for (var index = 1; index < exclusions.Length; index++)
            if (exclusions[index].PageNumber == exclusions[index - 1].PageNumber
                && exclusions[index].StartOffset < exclusions[index - 1].EndOffset)
                throw new InvalidDataException("Reviewed source exclusions must not overlap or repeat.");
        return review with { Exclusions = exclusions };
    }

    private static bool ExcludeMarkdownBlock(
        MarkdownSource source,
        Block block,
        DocumentMarkdownSourceReview? review,
        HashSet<DocumentMarkdownExclusion> matched
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
            matched.Add(annotation);
        return true;
    }
}
