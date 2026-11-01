#if DEBUG
namespace CasCap.Models;

/// <summary>One database-time-limited indexing attempt; never permission to replay an unknown batch.</summary>
internal sealed record DocumentIndexingClaim(string JobId, Guid ClaimId, DateTimeOffset ExpiresAtUtc, DocumentIndexingManifest Manifest);
#endif
