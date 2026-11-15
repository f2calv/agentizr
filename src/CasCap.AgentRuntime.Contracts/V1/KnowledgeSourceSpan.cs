namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Identifies a half-open UTF-16 block range in the reviewed page Markdown, not PDF coordinates.</summary>
public sealed record KnowledgeSourceSpan
{
    /// <summary>Gets the physical PDF page number, starting at one.</summary>
    /// <example>2</example>
    public required int PageNumber { get; init; }

    /// <summary>Gets the block ordinal within the reviewed page.</summary>
    /// <example>0</example>
    public required int BlockOrdinal { get; init; }

    /// <summary>Gets the inclusive Markdown offset.</summary>
    /// <example>0</example>
    public required int StartOffset { get; init; }

    /// <summary>Gets the exclusive Markdown offset.</summary>
    /// <example>120</example>
    public required int EndOffset { get; init; }
}
