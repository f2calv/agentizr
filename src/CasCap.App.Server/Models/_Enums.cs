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
