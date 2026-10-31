using System.Text.Json;

namespace CasCap.Models;

/// <summary>Captures the parser, projection and complete-input budgets independently of the unchanged page-local profile.</summary>
internal sealed record DocumentMarkdownChunkingProfile(
    string Strategy,
    int Version,
    string ParserVersion,
    string ParserOptions,
    string TokenizerEncoding,
    string TokenizerVersion,
    int TargetTokensPerChunk,
    int MaximumTokensPerChunk,
    int OverlapTokens,
    string HeadingPrefixVersion)
{
    /// <summary>Gets the immutable identity of all interpretation and projection settings.</summary>
    public string Id => DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(new
    {
        Strategy,
        Version,
        ParserVersion,
        ParserOptions,
        TokenizerEncoding,
        TokenizerVersion,
        TargetTokensPerChunk,
        MaximumTokensPerChunk,
        OverlapTokens,
        HeadingPrefixVersion,
    }));
}
