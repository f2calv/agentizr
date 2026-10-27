#if DEBUG
using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CasCap.Tests;

/// <summary>Generates synthetic PDFs without committing or downloading source documents.</summary>
internal static class DocumentPreparationTestData
{
    /// <summary>Builds one page per supplied text; empty text produces a blank page.</summary>
    public static byte[] Pdf(params string[] pages)
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

    /// <summary>Draws two independent columns in interleaved content-stream order.</summary>
    public static byte[] TwoColumns() => PositionedPdf(
        ("LEFT first entry.", 25, 700),
        ("RIGHT first entry.", 330, 700),
        ("LEFT second entry.", 25, 680),
        ("RIGHT second entry.", 330, 680));

    /// <summary>Draws a row-oriented vocabulary table; reading down each column loses word/example associations.</summary>
    public static byte[] VocabularyTable() => PositionedPdf(
        ("WORD", 25, 700), ("EXAMPLE", 330, 700),
        ("die Tuer", 25, 680), ("Die Tuer ist offen.", 330, 680),
        ("das Licht", 25, 660), ("Das Licht ist aus.", 330, 660));

    /// <summary>Draws an unstyled headword/example gutter with wrapped and additional example lines.</summary>
    public static byte[] DictionaryEntries(double exampleLeft) => PositionedPdf(
        ("the first, -s", 25, 700), ("First example ends", exampleLeft, 700),
        ("with a continuation!", exampleLeft, 687),
        ("second", 25, 674), ("Second example.", exampleLeft, 674),
        ("another example.", exampleLeft, 661),
        ("the third, -s", 25, 648), ("Third example.", exampleLeft, 648));

    /// <summary>Draws an emphasized heading, wrapped prose, a separate paragraph and repeated page furniture.</summary>
    public static byte[] LayoutPages(int count)
    {
        using var builder = new PdfDocumentBuilder();
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);
        for (var index = 0; index < count; index++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText("1 OVERVIEW", 16, new PdfPoint(25, 700), bold);
            page.AddText("First paragraph", 10, new PdfPoint(25, 650), regular);
            page.AddText("continues here.", 10, new PdfPoint(25, 635), regular);
            page.AddText("Second paragraph.", 10, new PdfPoint(25, 600), regular);
            page.AddText("UNIQUE TITLE " + new string((char)('A' + index), 1), 10, new PdfPoint(25, 800), bold);
            page.AddText($"PUBLICATION 1000 {index + 1}", 8, new PdfPoint(25, 25), regular);
            page.AddText($"SIDE {index + 1}", 6, new PdfPoint(5, 500), regular);
        }
        return builder.Build();
    }

    /// <summary>Draws a heading number and title as distant text objects plus one emphasized body phrase.</summary>
    public static byte[] SplitHeading()
    {
        using var builder = new PdfDocumentBuilder();
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);
        var page = builder.AddPage(PageSize.A4);
        page.AddText("SYNTHETIC SECTION", 16, new PdfPoint(70, 700), bold);
        page.AddText("2", 16, new PdfPoint(25, 700), bold);
        page.AddText("Important product", 10, new PdfPoint(25, 650), bold);
        page.AddText("description.", 10, new PdfPoint(120, 650), regular);
        return builder.Build();
    }

    /// <summary>Draws contents entries in one or two columns, with references at either edge.</summary>
    public static byte[] Contents(int columns, bool leadingReference)
    {
        using var builder = new PdfDocumentBuilder();
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);
        var page = builder.AddPage(PageSize.A4);
        page.AddText("CONTENTS", 16, new PdfPoint(25, 790), bold);
        for (var column = 0; column < columns; column++)
        {
            var left = 25 + column * 300;
            for (var row = 0; row < 3; row++)
            {
                var top = 740 - row * 25;
                page.AddText($"TOPIC {column + 1} {row + 1}", 10, new PdfPoint(left + 25, top), bold);
                page.AddText("....................", 10, new PdfPoint(left + 140, top), regular);
                page.AddText($"{row + column * 3 + 3}", 10, new PdfPoint(left + (leadingReference ? 0 : 245), top), regular);
            }
        }
        return builder.Build();
    }

    /// <summary>Draws consecutive list groups with heading-aligned right-column items and separately placed bullet markers.</summary>
    public static byte[] BandedLists()
    {
        using var builder = new PdfDocumentBuilder();
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);
        var page = builder.AddPage(PageSize.A4);
        page.AddText("FIRST GROUP", 10, new PdfPoint(25, 700), bold);
        page.AddText("SECOND GROUP", 10, new PdfPoint(25, 600), bold);
        foreach (var (text, x, y) in new[]
        {
            ("First item.", 25, 680), ("Second item.", 25, 660),
            ("Third item.", 330, 700), ("Fourth item.", 330, 680),
            ("Later item.", 25, 580), ("Final item.", 330, 600),
        })
        {
            page.AddText("-", 10, new PdfPoint(x, y), regular);
            page.AddText(text, 10, new PdfPoint(x + 15, y), regular);
        }
        return builder.Build();
    }

    /// <summary>Draws tightly spaced example rows that ordinary paragraph reconstruction would join.</summary>
    public static byte[] ExampleRows(params string[] lines) => PositionedPdf(
        lines.Select((text, index) => (text, 25d, 700 - index * 13d)).ToArray());

    /// <summary>Draws WinAnsi reference lines, including umlauts unsupported by the builder's Standard14 writer.</summary>
    public static byte[] ReferenceLines(params string[] lines)
    {
        var commands = string.Join("\n", lines.Select((text, index) =>
            FormattableString.Invariant($"BT /F1 10 Tf 25 {700 - index * 20} Td <{Convert.ToHexString(Encoding.Latin1.GetBytes(text))}> Tj ET")));
        return RawPdf([
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            FormattableString.Invariant($"<< /Length {commands.Length} >>\nstream\n{commands}\nendstream"),
        ]);
    }

    /// <summary>Builds an ASCII PDF with exact cross-reference offsets and optional test-owned trailer fields.</summary>
    public static byte[] RawPdf(IReadOnlyList<string> objects, string trailerFields = "")
    {
        var content = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(content.Length);
            content.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = content.Length;
        content.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            content.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        content.Append(CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R {trailerFields} >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(content.ToString());
    }

    private static byte[] PositionedPdf(params (string Text, double X, double Y)[] lines)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        foreach (var (text, x, y) in lines)
            page.AddText(text, 10, new PdfPoint(x, y), font);
        return builder.Build();
    }
}
#endif
