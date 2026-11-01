using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Computes stable byte and tuple identities without retaining source content in diagnostic identifiers.</summary>
internal static class DocumentPreparationIdentity
{
    /// <summary>Hashes exact source or derived request bytes.</summary>
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Hashes exact UTF-8 plan, draft or response text.</summary>
    public static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));

    /// <summary>Hashes unambiguous logical request components.</summary>
    public static string Digest(params string?[] values) => Hash(JsonSerializer.SerializeToUtf8Bytes(values));
}
