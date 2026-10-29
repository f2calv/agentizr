using System.Security.Cryptography;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Shares the existing canonical source and receipt digest format across event admission and synchronization.</summary>
internal static class DocumentIngestionIdentity
{
    /// <summary>Hashes ordered, nullable identity components without delimiter ambiguity.</summary>
    public static string Digest(params string?[] values) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values)));

    /// <summary>Compares storage/event ETags independently of their optional HTTP quote delimiters.</summary>
    public static bool SameETag(string? left, string? right) =>
        left is not null && right is not null
        && string.Equals(left.Trim('"'), right.Trim('"'), StringComparison.Ordinal);
}
