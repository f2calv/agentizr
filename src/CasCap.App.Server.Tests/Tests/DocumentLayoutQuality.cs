#if DEBUG
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace CasCap.Tests;

/// <summary>Test-only spatial extraction comparison; not a Markdown, table or heading recognizer.</summary>
internal static partial class DocumentLayoutQuality
{
    /// <summary>Gets ordered text blocks without changing the production content-order extractor.</summary>
    public static string SpatialText(Page page)
    {
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance);
        var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        return string.Join("\n\n", UnsupervisedReadingOrderDetector.Instance.Get(blocks).Select(block => block.Text));
    }

    /// <summary>Collapses whitespace for independently specified phrase and reading-order expectations.</summary>
    public static string Normalize(string text) => Whitespace().Replace(text, " ").Trim();

    /// <summary>Checks ordered anchors without including document content in assertion diagnostics.</summary>
    public static bool InOrder(string text, IReadOnlyList<string> anchors)
    {
        var normalized = Normalize(text);
        var offset = 0;
        foreach (var anchor in anchors)
        {
            var index = normalized.IndexOf(Normalize(anchor), offset, StringComparison.Ordinal);
            if (index < 0)
                return false;
            offset = index + Normalize(anchor).Length;
        }
        return true;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
#endif
