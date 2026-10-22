namespace CasCap.Entities;

/// <summary>Durable turn outcome; its activity is retained separately from mutable session state.</summary>
public sealed class AgentRunEntity
{
    /// <summary>Gets or sets the turn identity.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the owning tenant.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the top-level agent.</summary>
    public required string AgentName { get; set; }

    /// <summary>Gets or sets the caller's session identifier.</summary>
    public required string SessionId { get; set; }

    /// <summary>Gets or sets the selected immutable definition version.</summary>
    public required string DefinitionVersion { get; set; }

    /// <summary>Gets or sets the effective model.</summary>
    public string? ModelName { get; set; }

    /// <summary>Gets or sets the lifecycle status.</summary>
    public required string Status { get; set; }

    /// <summary>Gets or sets when the turn started.</summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>Gets or sets when completion or interruption was recorded.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Gets or sets the exception type, never its message or stack.</summary>
    public string? ErrorType { get; set; }

    /// <summary>Gets or sets the projected output and usage summary.</summary>
    public string? SummaryJson { get; set; }
}
