namespace CasCap.Constants;

/// <summary>Durable PoC admission states; queued is not evidence of extraction or searchability.</summary>
public static class DocumentIngestionStates
{
    /// <summary>Accepted for future document processing.</summary>
    public const string Queued = nameof(Queued);

    /// <summary>Source deletion was observed and the document is revoked.</summary>
    public const string Deleted = nameof(Deleted);

    /// <summary>An older or equivalent notification was durably recorded without changing the document.</summary>
    public const string Ignored = nameof(Ignored);
}
