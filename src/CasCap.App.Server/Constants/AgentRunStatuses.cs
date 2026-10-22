namespace CasCap.Constants;

/// <summary>Persisted execution lifecycle values.</summary>
internal static class AgentRunStatuses
{
    /// <summary>A turn started but has no committed terminal status.</summary>
    public const string Started = nameof(Started);

    /// <summary>The turn and its resumable state were persisted.</summary>
    public const string Completed = nameof(Completed);

    /// <summary>The turn failed.</summary>
    public const string Failed = nameof(Failed);

    /// <summary>The turn was cancelled.</summary>
    public const string Cancelled = nameof(Cancelled);

    /// <summary>A later owner observed an unfinished turn after acquiring the session lock.</summary>
    public const string Interrupted = nameof(Interrupted);
}
