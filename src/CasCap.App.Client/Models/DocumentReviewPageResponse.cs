namespace CasCap.Client.Models;

/// <summary>One normalized source page with non-embedding review notes.</summary>
public sealed record DocumentReviewPageResponse
{
    /// <summary>Gets the physical one-based PDF page.</summary>
    /// <example>1</example>
    public required int Page { get; init; }

    /// <summary>Gets normalized source Markdown whose UTF-16 offsets the projection references.</summary>
    /// <example># Operation</example>
    public required string Markdown { get; init; }

    /// <summary>Gets preparation change notes, separate from embedding text.</summary>
    /// <example>[]</example>
    public required IReadOnlyList<string> Changes { get; init; }

    /// <summary>Gets preparation uncertainties, separate from embedding text.</summary>
    /// <example>[]</example>
    public required IReadOnlyList<string> Uncertainties { get; init; }
}
