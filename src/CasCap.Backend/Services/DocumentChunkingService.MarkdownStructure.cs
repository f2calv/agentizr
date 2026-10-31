using Markdig.Extensions.Tables;
using Markdig.Syntax;

namespace CasCap.Services;

internal sealed partial class DocumentChunkingService
{
    private static DocumentMarkdownHeadingReview? ReviewedMarkdownHeading(
        MarkdownSource source,
        Block block,
        DocumentMarkdownSourceReview? review,
        HashSet<(int PageNumber, int StartOffset, int EndOffset)> matched
    )
    {
        var spans = MapMarkdown(source, block.Span.Start, block.Span.End + 1, 0);
        var annotation = review?.Headings.FirstOrDefault(item => spans.Length == 1
            && item.PageNumber == spans[0].PageNumber && item.StartOffset == spans[0].StartOffset && item.EndOffset == spans[0].EndOffset);
        if (annotation is null)
            return null;
        if (block is not HeadingBlock && (block is not ParagraphBlock
            || source.Text.AsSpan(block.Span.Start, block.Span.Length).IndexOfAny('\r', '\n') >= 0))
            throw new InvalidDataException("A heading decision must identify a heading or a single-line paragraph.");
        matched.Add((annotation.PageNumber, annotation.StartOffset, annotation.EndOffset));
        return annotation;
    }

    private static MarkdownUnit? ReviewedMarkdownTable(
        MarkdownSource source,
        Block block,
        DocumentMarkdownSourceReview? review,
        HashSet<(int PageNumber, int StartOffset, int EndOffset)> matched,
        int ordinal,
        DocumentMarkdownHeading[] headings
    )
    {
        var spans = MapMarkdown(source, block.Span.Start, block.Span.End + 1, ordinal);
        var annotation = review?.LayoutTables.FirstOrDefault(item => spans.Length == 1
            && item.PageNumber == spans[0].PageNumber && item.StartOffset == spans[0].StartOffset && item.EndOffset == spans[0].EndOffset);
        if (annotation is null)
            return null;
        if (block is not Table table)
            throw new InvalidDataException("A layout-table decision must identify a complete pipe table.");
        var projection = ProjectMarkdownLayout(source, table, ordinal);
        matched.Add((annotation.PageNumber, annotation.StartOffset, annotation.EndOffset));
        return new(block.Span.Start, block.Span.End + 1, ordinal, headings, false, projection);
    }

    private static MarkdownProjection ProjectMarkdownLayout(MarkdownSource source, Table table, int ordinal)
    {
        var rows = table.Cast<TableRow>().ToArray();
        if (rows.Length < 2 || !rows[0].IsHeader || rows.Any(row => row.Count != rows[0].Count))
            throw new InvalidDataException("A reviewed layout table requires a rectangular table with an empty header.");
        var entries = new List<MarkdownProjection>();
        var covered = new bool[table.Span.Length];
        // Pipe-table ColumnIndex is unset; source child order defines the columns.
        foreach (var item in rows.SelectMany(row => row.Cast<TableCell>().Select((cell, column) => (cell, column, row.IsHeader)))
            .OrderBy(item => item.column))
        {
            var projection = ReadMarkdownLayoutCell(source, table, item.cell, item.IsHeader, ordinal);
            if (projection is not null)
                entries.Add(projection);
            if (!item.cell.Span.IsEmpty)
                Array.Fill(covered, true, item.cell.Span.Start - table.Span.Start, item.cell.Span.Length);
        }
        if (entries.Count == 0)
            throw new InvalidDataException("A reviewed layout table has no independent entries.");
        ValidateMarkdownLayoutSyntax(source.Text.AsSpan(table.Span.Start, table.Span.Length), covered);
        return new(string.Join('\n', entries.Select(item => item.Text)), entries.SelectMany(item => item.Spans).ToArray());
    }

    private static MarkdownProjection? ReadMarkdownLayoutCell(MarkdownSource source, Table table, TableCell cell, bool header, int ordinal)
    {
        if (cell.Span.IsEmpty && cell.All(child => child is ParagraphBlock { Inline: null or { FirstChild: null } }))
            return null;
        var start = cell.Span.Start;
        var end = cell.Span.End + 1;
        if (cell.ColumnSpan != 1 || cell.RowSpan != 1 || start < table.Span.Start
            || end > table.Span.End + 1 || end <= start)
            throw new InvalidDataException("A reviewed layout table contains unsupported cell geometry.");
        while (start < end && char.IsWhiteSpace(source.Text[start]))
            start++;
        while (end > start && char.IsWhiteSpace(source.Text[end - 1]))
            end--;
        if (start == end)
            return null;
        if (header)
            throw new InvalidDataException("A table with column labels cannot be projected as a headerless layout list.");
        return new("- " + source.Text[start..end], MapMarkdown(source, start, end, ordinal));
    }

    private static void ValidateMarkdownLayoutSyntax(ReadOnlySpan<char> text, bool[] covered)
    {
        var line = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var value = text[index];
            if (value == '\n')
                line++;
            if (!covered[index] && !char.IsWhiteSpace(value) && value != '|' && !(line == 1 && value is '-' or ':'))
                throw new InvalidDataException("A reviewed layout table contains source content not mapped to parsed cells.");
        }
    }
}
