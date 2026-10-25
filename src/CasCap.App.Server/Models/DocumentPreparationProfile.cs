using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace CasCap.Models;

/// <summary>Captured algorithm, library and token-budget metadata for reproducible reprocessing decisions.</summary>
internal sealed record DocumentPreparationProfile(
    string Algorithm,
    string TokenizerEncoding,
    string ExtractorVersion,
    string TokenizerVersion,
    int MaximumTokensPerChunk,
    int OverlapTokens)
{
    /// <summary>Gets a deterministic profile identity; changing a captured setting changes this identity.</summary>
    public string Id => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[]
    {
        Algorithm, TokenizerEncoding, ExtractorVersion, TokenizerVersion,
        MaximumTokensPerChunk.ToString(CultureInfo.InvariantCulture), OverlapTokens.ToString(CultureInfo.InvariantCulture),
    })));
}
