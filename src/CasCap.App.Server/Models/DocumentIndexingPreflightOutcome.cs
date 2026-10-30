#if DEBUG
using CasCap.Client.Models;

namespace CasCap.Models;

/// <summary>Distinguishes unavailable storage, absent sources and successful non-authorizing preflight checks.</summary>
public sealed record DocumentIndexingPreflightOutcome
{
    /// <summary>Gets whether database-backed review is available.</summary>
    public bool IsSupported { get; init; } = true;

    /// <summary>Gets the metadata-only check, or null when the source/snapshot is unavailable.</summary>
    public DocumentIndexingPreflightResponse? Preflight { get; init; }
}
#endif
