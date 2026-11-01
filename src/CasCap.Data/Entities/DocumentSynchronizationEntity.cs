namespace CasCap.Entities;

/// <summary>Durable, coalesced request, fenced run and most recent progress for one configured source.</summary>
public sealed class DocumentSynchronizationEntity
{
    /// <summary>Gets or sets the trusted source digest.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the owning application.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the owning collection.</summary>
    public required string CollectionId { get; set; }

    /// <summary>Gets or sets whether another run has been durably requested.</summary>
    public bool Requested { get; set; }

    /// <summary>Gets or sets the latest request time.</summary>
    public DateTimeOffset RequestedAtUtc { get; set; }

    /// <summary>Gets or sets the current run's fencing identity.</summary>
    public Guid? RunId { get; set; }

    /// <summary>Gets or sets the current run's database-authoritative deadline.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>Gets or sets the latest run start.</summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>Gets or sets the latest run completion.</summary>
    public DateTimeOffset? FinishedAtUtc { get; set; }

    /// <summary>Gets or sets the latest successful full synchronization.</summary>
    public DateTimeOffset? LastSucceededAtUtc { get; set; }

    /// <summary>Gets or sets the durable run state.</summary>
    public required string State { get; set; }

    /// <summary>Gets or sets the number of registered originals in the latest run.</summary>
    public int AddedCount { get; set; }

    /// <summary>Gets or sets the number of repaired work items in the latest run.</summary>
    public int RepairedCount { get; set; }

    /// <summary>Gets or sets the number of retired sources in the latest run.</summary>
    public int RemovedCount { get; set; }

    /// <summary>Gets or sets the number of unchanged originals in the latest run.</summary>
    public int UnchangedCount { get; set; }

    /// <summary>Gets or sets the number of conflicting source observations in the latest run.</summary>
    public int ConflictCount { get; set; }

    /// <summary>Gets or sets an allowlisted operational failure code, never a provider message.</summary>
    public string? FailureCode { get; set; }
}
