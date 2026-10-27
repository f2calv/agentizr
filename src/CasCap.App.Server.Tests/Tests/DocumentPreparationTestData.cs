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
}
#endif
