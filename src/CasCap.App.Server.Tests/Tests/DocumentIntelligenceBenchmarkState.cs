#if DEBUG
using System.Security.Cryptography;
using System.Text.Json;

namespace CasCap.Tests;

/// <summary>Persists write-once paid-operation receipts so an interrupted benchmark cannot silently resubmit a PDF.</summary>
internal static class DocumentIntelligenceBenchmarkState
{
    /// <summary>Uses the same source/page manifest to resume polling or reuse a completed result; ambiguous submissions require operator reconciliation.</summary>
    public static async Task<byte[]> AnalyzeAsync(
        DocumentIntelligenceQualityClient client,
        string directory,
        string stem,
        byte[] original,
        int[] pages,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(stem) || !stem.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            throw new ArgumentException("Benchmark names must be generic ASCII identifiers.", nameof(stem));
        var manifestPath = Path.Combine(directory, stem + ".request.json");
        var operationPath = Path.Combine(directory, stem + ".operation.json");
        var resultPath = Path.Combine(directory, stem + ".analysis.json");
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            SourceSha256 = Convert.ToHexString(SHA256.HashData(original)),
            Pages = pages,
            DocumentIntelligenceQualityClient.ApiVersion,
            DocumentIntelligenceQualityClient.ModelId,
        });
        if (File.Exists(manifestPath))
        {
            var existing = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
            if (!existing.AsSpan().SequenceEqual(manifest))
                throw new InvalidOperationException("Benchmark source/page contract changed; existing paid-operation state cannot be reused.");
            if (File.Exists(resultPath))
            {
                if (new FileInfo(resultPath).Length > DocumentIntelligenceQualityClient.MaximumResponseBytes)
                    throw new InvalidOperationException("Saved managed response exceeds the byte limit.");
                var cached = await File.ReadAllBytesAsync(resultPath, cancellationToken);
                _ = DocumentIntelligenceQualityClient.ValidateResult(cached, pages);
                return cached;
            }
            if (!File.Exists(operationPath))
                throw new InvalidOperationException("A previous submission has no saved receipt; outcome is unknown. Reconcile before authorizing another paid submission.");
        }
        else
        {
            if (File.Exists(operationPath) || File.Exists(resultPath))
                throw new InvalidOperationException("Benchmark result/receipt has no matching source manifest.");
            await WriteNewAsync(manifestPath, manifest, cancellationToken);
            var submitted = await client.SubmitAsync(original, pages, cancellationToken);
            await WriteNewAsync(operationPath, JsonSerializer.SerializeToUtf8Bytes(new { Operation = submitted.AbsoluteUri }), cancellationToken);
        }
        using var receipt = JsonDocument.Parse(await File.ReadAllBytesAsync(operationPath, cancellationToken));
        if (receipt.RootElement.ValueKind != JsonValueKind.Object
            || !receipt.RootElement.TryGetProperty("Operation", out var location) || location.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(location.GetString(), UriKind.Absolute, out var operation))
            throw new InvalidOperationException("Saved managed operation receipt is invalid; do not resubmit automatically.");
        var result = await client.PollAsync(operation, pages, cancellationToken);
        await WriteNewAsync(resultPath, result, cancellationToken);
        return result;
    }

    private static async Task WriteNewAsync(string path, byte[] payload, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, useAsync: true);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
#endif
