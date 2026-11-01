#if DEBUG
namespace CasCap.Models;

/// <summary>Preserves the original included candidate, provenance and exact UTF-8 embedding-input identity.</summary>
internal sealed record DocumentIndexingInput(DocumentMarkdownChunk Chunk, string InputSha256);
#endif
