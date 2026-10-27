#if DEBUG
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace CasCap.Tests;

/// <summary>Builds explicit section and row-association candidates without replacing production extraction or claiming semantic acceptance.</summary>
internal static partial class DocumentLayoutReview
{
    /// <summary>Identifies the experimental layout algorithm independently from production preparation profiles.</summary>
    internal const string Version = "LayoutReviewV6";

    /// <summary>Captured page-local words and ordered spatial blocks; coordinates use the PDF's bottom-left origin.</summary>
    internal sealed record Snapshot(int Number, double Width, double Height, IReadOnlyList<Word> Words, IReadOnlyList<TextBlock> Blocks);

    /// <summary>Source coordinates for one retained line in one candidate view.</summary>
    internal sealed record Provenance(string View, int Ordinal, double Left, double Bottom, double Right, double Top);

    /// <summary>Inspectable candidates, excluded margin text and coverage/provenance diagnostics for one page.</summary>
    internal sealed record Result(
        string Sections,
        string Entries,
        string ExcludedMargins,
        int SourceWords,
        int RetainedWords,
        IReadOnlyList<Provenance> Coordinates);

    /// <summary>Captures source geometry once; does not infer document type from a source's application.</summary>
    public static Snapshot Capture(Page page)
    {
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance).Where(word => !string.IsNullOrWhiteSpace(word.Text)).ToArray();
        var blocks = words.Length == 0 ? [] : UnsupervisedReadingOrderDetector.Instance
            .Get(DocstrumBoundingBoxes.Instance.GetBlocks(words)).ToArray();
        return new(page.Number, page.Width, page.Height, words, blocks);
    }

    /// <summary>Renders both candidates using document-wide repetition evidence for conservative margin classification.</summary>
    public static IReadOnlyList<Result> Render(IReadOnlyList<Snapshot> pages, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = pages.Select(page =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Rows(page.Words);
        }).ToArray();
        var repeated = pages.SelectMany((page, index) => rows[index]
            .Where(row => IsMargin(row, page))
            .Select(row => (Page: page.Number, Key: MarginKey(row, page))))
            .GroupBy(item => item.Key)
            .Where(group => group.Select(item => item.Page).Distinct().Count() >= 2)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var results = new List<Result>(pages.Count);
        for (var index = 0; index < pages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pages[index];
            var margins = rows[index].Where(row => IsMargin(row, page) && repeated.Contains(MarginKey(row, page))).ToArray();
            var excluded = new HashSet<Word>(margins.SelectMany(row => row), ReferenceEqualityComparer.Instance);
            var coordinates = new List<Provenance>();
            var sectionWords = new List<Word>();
            var sections = Sections(page, excluded, coordinates, sectionWords);
            var retainedRows = rows[index].Select(row => row.Where(word => !excluded.Contains(word)).ToArray())
                .Where(row => row.Length > 0).ToArray();
            var entries = new StringBuilder();
            foreach (var row in retainedRows)
            {
                coordinates.Add(Bounds("entry-row", coordinates.Count, row));
                entries.Append(Line(row)).Append("\n\n");
            }
            var retained = page.Words.Count - excluded.Count;
            var expected = page.Words.Where(word => !excluded.Contains(word)).ToArray();
            if (sectionWords.Count != retained || retainedRows.Sum(row => row.Length) != retained
                || !new HashSet<Word>(sectionWords, ReferenceEqualityComparer.Instance).SetEquals(expected)
                || !new HashSet<Word>(retainedRows.SelectMany(row => row), ReferenceEqualityComparer.Instance).SetEquals(expected))
                throw new InvalidOperationException("Layout candidate lost or duplicated source words; document details withheld.");
            results.Add(new(sections, entries.ToString(), string.Join("\n\n", margins.Select(row => Line(row))),
                page.Words.Count, retained, coordinates));
        }
        return results;
    }

    private static string Sections(Snapshot page, HashSet<Word> excluded, List<Provenance> coordinates, List<Word> coverage)
    {
        var retained = page.Words.Where(word => !excluded.Contains(word)).ToArray();
        if (retained.Any(word => ContentsHeading().IsMatch(word.Text) && IsBold(word)
            && word.BoundingBox.Bottom > page.Height * .75))
            return Contents(page, retained, coordinates, coverage);
        var output = new StringBuilder();
        var sizes = page.Words.SelectMany(word => word.Letters).Select(letter => letter.PointSize).Where(size => size > 0).Order().ToArray();
        var bodySize = sizes.Length == 0 ? 0 : sizes[sizes.Length / 2];
        foreach (var block in SectionLines(page, excluded, bodySize))
        {
            var examples = block.Count(line => line.Words.Any(word => word.Text.Contains('='))) >= 2
                || block.Any(line => AlphabeticalMarker().IsMatch(line.Words[0].Text));
            var compactRows = CompactListRows(block, bodySize);
            TextLine? previous = null;
            var previousEmphasis = false;
            var previousList = false;
            foreach (var line in block)
            {
                var words = line.Words.Where(word => !excluded.Contains(word)).OrderBy(word => word.BoundingBox.Left).ToArray();
                if (words.Length == 0)
                    continue;
                coverage.AddRange(words);
                coordinates.Add(Bounds("section", coordinates.Count, words));
                var letters = words.SelectMany(word => word.Letters).ToArray();
                var emphasized = letters.Length > 0 && letters.All(letter => letter.FontDetails.IsBold);
                var numbered = NumberedHeading().Match(string.Join(" ", words.Select(word => word.Text)));
                var heading = emphasized && words.Any(word => word.Text.Any(char.IsLetter))
                    && (numbered.Success || bodySize > 0 && letters.All(letter => letter.PointSize > bodySize * 1.2));
                var item = words.Length > 1 && ListMarker().IsMatch(words[0].Text);
                var paragraph = previous is null || item || emphasized || previousEmphasis || AlphabeticalMarker().IsMatch(words[0].Text)
                    || compactRows.Contains(line) != compactRows.Contains(previous)
                    || previous.BoundingBox.Bottom - line.BoundingBox.Top > Math.Max(bodySize,
                        Math.Max(previous.BoundingBox.Height, line.BoundingBox.Height)) * .9;
                if (output.Length > 0 && output[^1] != '\n')
                    output.Append(paragraph ? "\n\n" : previousList ? "\n  " : examples || compactRows.Contains(line) ? "  \n" : " ");
                if (heading)
                {
                    var depth = numbered.Success ? Math.Clamp(3 + numbered.Groups[1].Value.Count(character => character == '.'), 3, 6) : 3;
                    output.Append(new string('#', depth)).Append(' ');
                }
                if (item)
                    output.Append("- ");
                output.Append(Line(item ? words[1..] : words, emphasis: !heading));
                previous = line;
                previousEmphasis = emphasized;
                previousList = item || previousList && !paragraph;
            }
            output.Append("\n\n");
        }
        return output.ToString();
    }

    private static HashSet<TextLine> CompactListRows(TextLine[] lines, double bodySize)
    {
        var result = new HashSet<TextLine>();
        var run = new List<TextLine>();
        foreach (var line in lines)
        {
            var columnGap = line.Words.Zip(line.Words.Skip(1))
                .Any(pair => pair.Second.BoundingBox.Left - pair.First.BoundingBox.Right >= Math.Max(1, bodySize * 2));
            var compact = (line.Words.Count <= 3 || line.Words.Count <= 6 && columnGap)
                && line.Words.Any(word => word.Text.Any(char.IsLetter))
                && !SentenceEnd().IsMatch(line.Words[^1].Text);
            if (!compact || run.Count > 0 && Math.Abs(line.BoundingBox.Left - run[0].BoundingBox.Left) > Math.Max(1, bodySize * .5))
                run.Clear();
            if (!compact)
                continue;
            run.Add(line);
            if (run.Count == 3)
                result.UnionWith(run);
            else if (run.Count > 3)
                result.Add(line);
        }
        return result;
    }

    private static TextLine[][] SectionLines(Snapshot page, HashSet<Word> excluded, double bodySize)
    {
        var words = page.Blocks.SelectMany(block => block.TextLines).SelectMany(line => line.Words)
            .Where(word => !excluded.Contains(word)).ToArray();
        var rows = Rows(words);
        var gutter = rows.Count(row => GutterGap(row, page.Width) >= Math.Max(1, bodySize * 2)) >= 2;
        var starts = new List<int> { 0 };
        for (var index = 1; index < rows.Count; index++)
        {
            var left = rows[index].Where(word => word.BoundingBox.Centroid.X < page.Width / 2).ToArray();
            if (left.Length > 0 && left.All(IsBold) && left.Any(word => word.Text.Any(char.IsLetter)))
                starts.Add(index);
        }
        starts.Add(rows.Count);
        var result = new List<TextLine[]>();
        for (var index = 0; index < starts.Count - 1; index++)
        {
            var band = rows.Skip(starts[index]).Take(starts[index + 1] - starts[index]).ToArray();
            if (band.Length == 0)
                continue;
            if (band[0].All(IsBold))
            {
                result.Add([new TextLine(band[0], " ")]);
                band = band[1..];
            }
            var gaps = band.Select(row => GutterGap(row, page.Width)).Where(gap => gap is not null).ToArray();
            var entries = DictionaryLines(band, bodySize);
            if (entries is not null)
                result.AddRange(entries);
            else if (gutter && gaps.Length > 0 && gaps.All(gap => gap >= Math.Max(1, bodySize * 2)))
            {
                var bandWords = band.SelectMany(row => row).ToArray();
                result.Add(Rows(bandWords.Where(word => word.BoundingBox.Centroid.X < page.Width / 2).ToArray())
                    .Select(row => new TextLine(row, " ")).ToArray());
                result.Add(Rows(bandWords.Where(word => word.BoundingBox.Centroid.X >= page.Width / 2).ToArray())
                    .Select(row => new TextLine(row, " ")).ToArray());
            }
            else if (band.Length > 0)
                result.Add(band.Select(row => new TextLine(row, " ")).ToArray());
        }
        return result.Where(block => block.Length > 0).ToArray();
    }

    private static TextLine[][]? DictionaryLines(Word[][] rows, double bodySize)
    {
        var tolerance = Math.Max(1, bodySize * .5);
        var minimumGap = Math.Max(1, bodySize * 2);
        var gutter = rows.SelectMany(row => row.Select((word, index) => (Row: row, Index: index)).Skip(1))
            .Where(item => item.Index <= 4
                && item.Row[item.Index].BoundingBox.Left - item.Row[item.Index - 1].BoundingBox.Right >= minimumGap
                && item.Row.Take(item.Index).Any(word => word.Text.Any(char.IsLetter))
                && !item.Row.Take(item.Index).Any(word => word.Text.Contains('=') || ListMarker().IsMatch(word.Text))
                && !SentenceEnd().IsMatch(item.Row[item.Index - 1].Text))
            .GroupBy(item => Math.Round(item.Row[item.Index].BoundingBox.Left / tolerance))
            .Where(group => group.Count() >= 3)
            .OrderByDescending(group => group.Count()).FirstOrDefault();
        if (gutter is null)
            return null;
        var boundary = gutter.Min(item => item.Row[item.Index].BoundingBox.Left) - tolerance;
        foreach (var row in rows)
        {
            if (row.Any(word => word.BoundingBox.Left < boundary && word.BoundingBox.Right > boundary))
                return null;
            var left = row.Where(word => word.BoundingBox.Left < boundary).ToArray();
            var right = row.Where(word => word.BoundingBox.Left >= boundary).ToArray();
            if (left.Length > 0 && right.Length > 0
                && (left.Length > 4 || right[0].BoundingBox.Left - left[^1].BoundingBox.Right < minimumGap))
                return null;
        }
        var entries = new List<List<TextLine>>();
        foreach (var row in rows)
        {
            if (entries.Count == 0 || row[0].BoundingBox.Left < boundary)
                entries.Add([]);
            entries[^1].Add(new TextLine(row, " "));
        }
        return entries.Select(entry => entry.ToArray()).ToArray();
    }

    private static double? GutterGap(Word[] row, double width)
    {
        var left = row.Where(word => word.BoundingBox.Centroid.X < width / 2).ToArray();
        var right = row.Where(word => word.BoundingBox.Centroid.X >= width / 2).ToArray();
        return left.Length == 0 || right.Length == 0 ? null
            : right.Min(word => word.BoundingBox.Left) - left.Max(word => word.BoundingBox.Right);
    }

    private static string Contents(Snapshot page, Word[] words, List<Provenance> coordinates, List<Word> coverage)
    {
        var left = words.Where(word => word.BoundingBox.Centroid.X < page.Width / 2).ToArray();
        var right = words.Where(word => word.BoundingBox.Centroid.X >= page.Width / 2).ToArray();
        var columns = ContentsColumn(left) && ContentsColumn(right) ? new[] { left, right } : [words];
        var output = new StringBuilder();
        foreach (var column in columns)
        {
            foreach (var row in Rows(column))
            {
                coverage.AddRange(row);
                coordinates.Add(Bounds("contents-row", coordinates.Count, row));
                var title = row.Any(word => ContentsHeading().IsMatch(word.Text)) && row.Length <= 4;
                output.Append(title ? "### " : "- ").Append(Line(row, emphasis: !title, compactLeaders: true)).Append("\n\n");
            }
        }
        return output.ToString();
    }

    private static bool ContentsColumn(Word[] words) => Rows(words).Count(row =>
        row.Any(word => word.Text.Any(char.IsLetter))
        && (PageReference().IsMatch(row[0].Text) || PageReference().IsMatch(row[^1].Text))) >= 3;

    private static List<Word[]> Rows(IReadOnlyList<Word> words)
    {
        var result = new List<List<Word>>();
        var heights = words.Select(word => word.BoundingBox.Height).Where(height => height > 0).Order().ToArray();
        var tolerance = heights.Length == 0 ? 1 : Math.Max(1, heights[heights.Length / 2] * .4);
        var baseline = double.PositiveInfinity;
        foreach (var word in words.OrderByDescending(Baseline).ThenBy(word => word.BoundingBox.Left))
        {
            if (result.Count == 0 || baseline - Baseline(word) > tolerance)
            {
                result.Add([]);
                baseline = Baseline(word);
            }
            result[^1].Add(word);
        }
        return result.Select(row => row.OrderBy(word => word.BoundingBox.Left).ToArray()).ToList();
    }

    private static double Baseline(Word word) => word.Letters.Average(letter => letter.StartBaseLine.Y);

    private static bool IsMargin(Word[] row, Snapshot page) =>
        row.All(word => word.BoundingBox.Top < page.Height * .07)
        || row.All(word => word.BoundingBox.Bottom > page.Height * .93)
        || row.All(word => word.BoundingBox.Right < page.Width * .07)
        || row.All(word => word.BoundingBox.Left > page.Width * .93);

    private static string MarginKey(Word[] row, Snapshot page)
    {
        var region = row.All(word => word.BoundingBox.Top < page.Height * .07) ? "bottom"
            : row.All(word => word.BoundingBox.Bottom > page.Height * .93) ? "top"
            : row.All(word => word.BoundingBox.Right < page.Width * .07) ? "left" : "right";
        return region + ":" + Digits().Replace(DocumentLayoutQuality.Normalize(string.Join(" ", row.Select(word => word.Text))), "#");
    }

    private static Provenance Bounds(string view, int ordinal, Word[] words) => new(view, ordinal,
        words.Min(word => word.BoundingBox.Left), words.Min(word => word.BoundingBox.Bottom),
        words.Max(word => word.BoundingBox.Right), words.Max(word => word.BoundingBox.Top));

    private static bool IsBold(Word word) => word.Letters.Count > 0 && word.Letters.All(letter => letter.FontDetails.IsBold);

    private static string Line(Word[] words, bool emphasis = true, bool compactLeaders = false)
    {
        var output = new StringBuilder();
        for (var index = 0; index < words.Length;)
        {
            var bold = emphasis && IsBold(words[index]);
            var end = index + 1;
            while (end < words.Length && (emphasis && IsBold(words[end])) == bold)
                end++;
            if (output.Length > 0)
                output.Append(' ');
            if (bold)
                output.Append("**");
            for (var current = index; current < end; current++)
            {
                if (current > index)
                    output.Append(' ');
                var text = compactLeaders ? DotLeader().Replace(words[current].Text, "...") : words[current].Text;
                var escaped = Escape(text);
                if (current == 0 && OrderedMarker().IsMatch(text))
                    escaped = escaped.Insert(escaped.Length - 1, "\\");
                else if (current == 0 && text == "-")
                    escaped = "\\-";
                output.Append(escaped);
            }
            if (bold)
                output.Append("**");
            index = end;
        }
        return output.ToString();
    }

    private static string Escape(string text)
    {
        var output = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (@"\`*_{}[]<>#+|".Contains(character))
                output.Append('\\');
            output.Append(character);
        }
        return output.ToString();
    }

    [GeneratedRegex(@"^([0-9]+(?:\.[0-9]+)*)\s+\p{L}", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedHeading();

    [GeneratedRegex(@"^[0-9]+[.)]$", RegexOptions.CultureInvariant)]
    private static partial Regex OrderedMarker();

    [GeneratedRegex(@"^\p{L}\)$", RegexOptions.CultureInvariant)]
    private static partial Regex AlphabeticalMarker();

    [GeneratedRegex(@"[.!?]$", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"^[\u2022\u25CF\u25AA\u25E6\u2023\u25BA\u25B6-]$", RegexOptions.CultureInvariant)]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"^(?:CONTENTS|INHALT|INHALTSVERZEICHNIS)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ContentsHeading();

    [GeneratedRegex(@"^[.\u00B7\u2026]*[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PageReference();

    [GeneratedRegex(@"[.\u00B7\u2026]{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex DotLeader();

    [GeneratedRegex(@"[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();
}
#endif
