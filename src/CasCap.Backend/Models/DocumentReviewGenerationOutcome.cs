#if DEBUG
using CasCap.Client.Models;

namespace CasCap.Models;

/// <summary>Distinguishes unsupported storage, invalid decisions, source conflicts and absent review material.</summary>
public sealed record DocumentReviewGenerationOutcome
{
    /// <summary>Gets whether durable review storage is configured.</summary>
    public bool IsSupported { get; init; } = true;

    /// <summary>Gets whether the observed source revision has changed.</summary>
    public bool IsConflict { get; init; }

    /// <summary>Gets a safe input-validation message, never a receipt or database exception.</summary>
    public string? ValidationError { get; init; }

    /// <summary>Gets the authorized projection, or null when unavailable.</summary>
    public DocumentReviewGenerationResponse? Generation { get; init; }
}
#endif
