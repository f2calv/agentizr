#if DEBUG
using System.Net;

namespace CasCap.Tests;

/// <summary>Renders review metadata separately from transcription; usage stays in the receipt/export.</summary>
internal static class DocumentPreparationReviewRenderer
{
    public static string Render(DocumentPreparationCandidate candidate)
    {
        var changes = Notes(candidate.Changes);
        var uncertainties = Notes(candidate.Uncertainties);
        return $"<pre class='transcription'>{WebUtility.HtmlEncode(candidate.Markdown)}</pre>"
            + (uncertainties.Length == 0 ? string.Empty
                : $"<aside class='review-metadata'><h4>Page Uncertainties</h4><ul>{uncertainties}</ul></aside>")
            + (changes.Length == 0 ? string.Empty
                : $"<details class='review-metadata'><summary>Review changes (not document content)</summary><ul>{changes}</ul></details>");
    }

    private static string Notes(string[] notes) =>
        string.Concat(notes.Where(note => !string.IsNullOrWhiteSpace(note))
            .Select(note => $"<li>{WebUtility.HtmlEncode(note)}</li>"));
}
#endif
