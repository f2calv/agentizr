namespace CasCap.AgentRuntime.Execution.Models;

/// <summary>Session compaction policy for agents backed by one provider.</summary>
/// <remarks>
/// Tool calls and results are stripped from the history on every run. The sliding window trims the
/// history back to <see cref="TargetMessageCount"/> only once it exceeds
/// <see cref="TriggerMessageCount"/>, so the prefix stays stable between trims and provider prompt
/// caches stay warm.
/// </remarks>
public sealed record SessionCompactionConfig : IValidatableObject
{
    /// <summary>Gets the non-system message count kept when the sliding window trims.</summary>
    [Range(1, 1_000)]
    public int TargetMessageCount { get; init; } = 20;

    /// <summary>Gets the non-system message count that must be exceeded before the window trims.</summary>
    [Range(1, 1_000)]
    public int TriggerMessageCount { get; init; } = 20;

    /// <summary>Gets which compactions are reported as run events.</summary>
    public CompactionNotificationMode Notification { get; init; } = CompactionNotificationMode.WindowTrimmed;

    /// <summary>Gets whether a compaction pass should be reported under <see cref="Notification"/>.</summary>
    public bool ShouldNotify(CompactionStats stats) => Notification switch
    {
        CompactionNotificationMode.Always => true,
        CompactionNotificationMode.WindowTrimmed => stats.WindowTrimmed > 0,
        _ => false,
    };

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TriggerMessageCount < TargetMessageCount)
            yield return new ValidationResult(
                $"{nameof(TriggerMessageCount)} must be at least {nameof(TargetMessageCount)}.",
                [nameof(TriggerMessageCount)]);
    }
}
