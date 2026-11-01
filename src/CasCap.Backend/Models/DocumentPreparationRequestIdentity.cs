namespace CasCap.Models;

/// <summary>Immutable logical paid-request identity checked on every resume.</summary>
internal sealed record DocumentPreparationRequestIdentity(
    string SourceSha256,
    string PlanSha256,
    string RequestSha256,
    string Stage,
    int PageNumber,
    string? DerivedPdfSha256 = null);
