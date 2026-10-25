namespace CasCap.Models;

/// <summary>Expected processor outcomes; unknown faults must propagate rather than request a retry.</summary>
internal enum DocumentProcessingOutcome
{
    /// <summary>The real processor finished its work.</summary>
    Completed,

    /// <summary>A known recoverable failure permits a bounded retry.</summary>
    RetryableFailure,

    /// <summary>A known permanent failure must not be retried.</summary>
    PermanentFailure,
}

/// <summary>Explicit local preparation outcomes; prepared text is not embedded or searchable content.</summary>
internal enum PdfPreparationStatus
{
    /// <summary>All text-bearing pages were prepared within the selected limits.</summary>
    Prepared,

    /// <summary>The input is not a supported well-formed PDF.</summary>
    InvalidPdf,

    /// <summary>Encrypted input is not admitted, including documents readable with an empty password.</summary>
    EncryptedPdf,

    /// <summary>The source exceeds the byte budget.</summary>
    ByteLimitExceeded,

    /// <summary>The source exceeds the page budget.</summary>
    PageLimitExceeded,

    /// <summary>The extracted text exceeds the character budget.</summary>
    TextLimitExceeded,

    /// <summary>The complete output would exceed the chunk budget.</summary>
    ChunkLimitExceeded,

    /// <summary>No page produced usable text; OCR may be needed.</summary>
    NoExtractableText,
}
