#if DEBUG
namespace CasCap.Models;

/// <summary>Ordered embedding vectors and native billed-input usage, never emitted to diagnostics.</summary>
internal sealed record DocumentEmbeddingResult(IReadOnlyList<float[]> Vectors, long InputTokens);
#endif
