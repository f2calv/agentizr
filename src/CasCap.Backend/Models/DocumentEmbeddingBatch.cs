#if DEBUG
namespace CasCap.Models;

/// <summary>An exact reserved batch whose intent is committed before a paid submission.</summary>
internal sealed record DocumentEmbeddingBatch(DocumentIndexingClaim Claim, int Ordinal, IReadOnlyList<DocumentIndexingInput> Inputs);
#endif
