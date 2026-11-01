namespace CasCap.Constants;

/// <summary>Fixed bounds and content-free failure codes for the serial processing PoC.</summary>
internal static class DocumentProcessingPolicy
{
    /// <summary>Maximum attempts, including abandoned claims.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Persisted delay before another attempt.</summary>
    public const int RetryDelaySeconds = 30;

    /// <summary>The processor explicitly reported a recoverable failure.</summary>
    public const string RetryableFailure = nameof(RetryableFailure);

    /// <summary>The processor explicitly reported a permanent failure.</summary>
    public const string PermanentFailure = nameof(PermanentFailure);

    /// <summary>The processing deadline passed, including an abandoned claim.</summary>
    public const string DeadlineExpired = nameof(DeadlineExpired);
}
