#if DEBUG
using CasCap.Client.Models;

namespace CasCap.Models;

/// <summary>Distinguishes durable acceptance/retry from unsupported storage, conflicts and invalid authorization.</summary>
public sealed record DocumentIndexingJobOutcome
{
    /// <summary>Gets whether native persistence is available.</summary>
    public bool IsSupported { get; init; } = true;

    /// <summary>Gets whether the exact authorization or observed generation conflicts.</summary>
    public bool IsConflict { get; init; }

    /// <summary>Gets whether this request durably created a job.</summary>
    public bool Created { get; init; }

    /// <summary>Gets a payload-free authorization validation error.</summary>
    public string? ValidationError { get; init; }

    /// <summary>Gets durable job progress, if found.</summary>
    public DocumentIndexingJobResponse? Job { get; init; }
}
#endif
