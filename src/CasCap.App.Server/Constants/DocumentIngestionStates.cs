namespace CasCap.Constants;

/// <summary>Durable admission and job states; none establishes document searchability.</summary>
public static class DocumentIngestionStates
{
    /// <summary>Accepted for future document processing.</summary>
    public const string Queued = nameof(Queued);

    /// <summary>Source deletion was observed and the document is revoked.</summary>
    public const string Deleted = nameof(Deleted);

    /// <summary>An older or equivalent notification was durably recorded without changing the document.</summary>
    public const string Ignored = nameof(Ignored);

    /// <summary>A bounded processing attempt owns the job.</summary>
    public const string Processing = nameof(Processing);

    /// <summary>The job is waiting for its persisted retry time.</summary>
    public const string RetryPending = nameof(RetryPending);

    /// <summary>The registered processor finished; this is not a searchable-generation pointer.</summary>
    public const string Completed = nameof(Completed);

    /// <summary>A permanent failure or the attempt limit ended processing.</summary>
    public const string Failed = nameof(Failed);
}
