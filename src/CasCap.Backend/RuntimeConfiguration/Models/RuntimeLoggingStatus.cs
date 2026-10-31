namespace CasCap.RuntimeConfiguration.Models;

/// <summary>Payload-free per-process evidence of the actual logging switch, not durable revision/expiry metadata.</summary>
public sealed record RuntimeLoggingStatus
{
    /// <summary>Gets a random process-lifetime probe identity, unrelated to users or documents.</summary>
    public required Guid InstanceId { get; init; }

    /// <summary>Gets the immutable default captured before the first runtime value is applied.</summary>
    public required string StartupMinimumLevel { get; init; }

    /// <summary>Gets the current minimum from the same switch used by the Serilog pipeline.</summary>
    public required string EffectiveMinimumLevel { get; init; }

    /// <summary>Gets whether the real logging pipeline currently accepts Debug for the probe category.</summary>
    public required bool ProbeDebugEnabled { get; init; }

    /// <summary>Gets the count of actual monitor-value applications, including startup.</summary>
    public required long ApplyCount { get; init; }

    /// <summary>Gets the time of the latest application, or null before startup application.</summary>
    public DateTimeOffset? LastAppliedAtUtc { get; init; }
}
