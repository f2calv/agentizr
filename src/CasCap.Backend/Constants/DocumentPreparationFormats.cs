namespace CasCap.Constants;

/// <summary>Versioned preparation identifiers; future tokenizer implementations require a distinct profile.</summary>
internal static class DocumentPreparationFormats
{
    /// <summary>Page-local windows over reading-order text with Unicode scalar-safe boundaries.</summary>
    public const string PageLocalV1 = nameof(PageLocalV1);

    /// <summary>Source heading paths prefixed to bounded Markdown body chunks.</summary>
    public const string MarkdownHeadings = "markdown-headings";

    /// <summary>The explicitly packaged tokenizer encoding; not a universal model tokenizer.</summary>
    public const string Cl100kBase = "cl100k_base";
}
