namespace CasCap.Entities;

/// <summary>One committed message or tool activity, including activity before a failed turn.</summary>
public sealed class AgentRunActivityEntity
{
    /// <summary>Gets or sets the database-assigned activity ordering key.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the owning turn.</summary>
    public Guid RunId { get; set; }

    /// <summary>Gets or sets the recording time.</summary>
    public DateTimeOffset RecordedAtUtc { get; set; }

    /// <summary>Gets or sets the sanitized activity projection.</summary>
    public required string ActivityJson { get; set; }
}
