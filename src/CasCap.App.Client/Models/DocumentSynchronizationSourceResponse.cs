namespace CasCap.Client.Models;

/// <summary>Returns safe progress for one configured source, not a Blob location or processing plan.</summary>
public sealed record DocumentSynchronizationSourceResponse
{
    /// <summary>Gets an opaque source identity distinguishing containers in the same collection.</summary>
    /// <example>example-source-digest</example>
    public required string SourceId { get; init; }

    /// <summary>Gets the application-local collection identity.</summary>
    /// <example>manuals</example>
    public required string CollectionId { get; init; }

    /// <summary>Gets NotRun, Queued, Running, Succeeded or Failed.</summary>
    /// <example>Queued</example>
    public required string State { get; init; }

    /// <summary>Gets the latest durable request time, if any.</summary>
    /// <example>2030-01-01T01:00:00Z</example>
    public DateTimeOffset? RequestedAtUtc { get; init; }

    /// <summary>Gets the most recent run start.</summary>
    /// <example>2030-01-01T01:00:01Z</example>
    public DateTimeOffset? StartedAtUtc { get; init; }

    /// <summary>Gets the most recent run finish.</summary>
    /// <example>2030-01-01T01:00:05Z</example>
    public DateTimeOffset? FinishedAtUtc { get; init; }

    /// <summary>Gets the most recent fully successful scan time; conflicts may still require attention.</summary>
    /// <example>2030-01-01T01:00:05Z</example>
    public DateTimeOffset? LastSucceededAtUtc { get; init; }

    /// <summary>Gets the latest run's newly admitted source count.</summary>
    /// <example>1</example>
    public int AddedCount { get; init; }

    /// <summary>Gets the latest run's repaired work count.</summary>
    /// <example>0</example>
    public int RepairedCount { get; init; }

    /// <summary>Gets the latest run's retired source count.</summary>
    /// <example>0</example>
    public int RemovedCount { get; init; }

    /// <summary>Gets the latest run's unchanged source count.</summary>
    /// <example>2</example>
    public int UnchangedCount { get; init; }

    /// <summary>Gets observations that could not safely overwrite a concurrent change or tombstone.</summary>
    /// <example>0</example>
    public int ConflictCount { get; init; }

    /// <summary>Gets an allowlisted failure code, never a provider message.</summary>
    /// <example>StorageUnavailable</example>
    public string? FailureCode { get; init; }
}
