namespace CasCap.Models;

/// <summary>A bounded, nonpersistent preview projection tied to exact page Markdown; not approval or a PDF source digest.</summary>
internal sealed record DocumentMarkdownChunkingResult(
    DocumentMarkdownChunkingProfile Profile,
    string MarkdownSha256,
    IReadOnlyList<DocumentMarkdownChunk> Chunks);
