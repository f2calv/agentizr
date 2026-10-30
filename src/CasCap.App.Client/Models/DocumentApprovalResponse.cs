namespace CasCap.Client.Models;

/// <summary>An audited readiness assessment whose currency is fenced by the saved chunk review.</summary>
public sealed record DocumentApprovalResponse
{
    /// <summary>Gets the append-only assessment revision.</summary>
    /// <example>1</example>
    public required long Revision { get; init; }

    /// <summary>Gets the database recording time.</summary>
    /// <example>2026-01-01T00:00:00Z</example>
    public required DateTimeOffset RecordedAtUtc { get; init; }

    /// <summary>Gets the recorded classification and reviewer attestations.</summary>
    /// <example>{}</example>
    public required DocumentApprovalRequest Assessment { get; init; }

    /// <summary>Gets whether this assessment still refers to the current saved selection.</summary>
    /// <example>true</example>
    public required bool IsCurrent { get; init; }

    /// <summary>Gets unresolved gates, including a changed chunk selection.</summary>
    /// <example>[]</example>
    public required IReadOnlyList<string> Blockers { get; init; }

    /// <summary>Gets whether this current assessment has resolved its supported readiness gates.</summary>
    /// <example>false</example>
    public bool ReadinessApproved => IsCurrent && Blockers.Count == 0;

    /// <summary>Gets whether indexing is authorized; this slice never grants publication eligibility.</summary>
    /// <example>false</example>
    public bool IndexingApproved => false;
}
