namespace CasCap.Constants;

/// <summary>Durable document synchronization states and sanitized failure codes.</summary>
internal static class DocumentSynchronizationStates
{
    /// <summary>A durable request is waiting for the ingestion owner.</summary>
    public const string Queued = nameof(Queued);

    /// <summary>The ingestion owner holds a fenced synchronization run.</summary>
    public const string Running = nameof(Running);

    /// <summary>The complete source inventory and absence checks finished.</summary>
    public const string Succeeded = nameof(Succeeded);

    /// <summary>The run failed and requires an explicit or scheduled retry.</summary>
    public const string Failed = nameof(Failed);

    /// <summary>Storage metadata could not be read.</summary>
    public const string StorageUnavailable = nameof(StorageUnavailable);

    /// <summary>The cooperative synchronization deadline expired.</summary>
    public const string DeadlineExpired = nameof(DeadlineExpired);
}
