#if DEBUG
using System.Text.Json;

namespace CasCap.Tests;

/// <summary>Runs bounded, explicitly approved layout probes without wiring managed extraction into production ingestion.</summary>
// TODO: Move the accepted transport contract into the shared Azure library when the managed preparation adapter is implemented; keep sample budgets and review policy here.
internal sealed class DocumentIntelligenceQualityClient(HttpClient httpClient)
{
    /// <summary>The pinned layout REST contract used by the benchmark.</summary>
    internal const string ApiVersion = "2024-11-30";
    /// <summary>The layout model selected independently of chat and embedding models.</summary>
    internal const string ModelId = "prebuilt-layout";
    /// <summary>The maximum response size accepted by the private benchmark.</summary>
    internal const int MaximumResponseBytes = 16 * 1024 * 1024;
    private const string ResultPath = "/documentintelligence/documentModels/" + ModelId + "/analyzeResults/";

    /// <summary>Preserves complete Markdown and page-local UTF-16 span projections for visual comparison.</summary>
    internal sealed record Result(string Markdown, IReadOnlyDictionary<int, string> Pages);

    /// <summary>Submits exactly once; a transport failure can leave the remote outcome unknown and must not be automatically replayed.</summary>
    public async Task<Uri> SubmitAsync(byte[] pdf, int[] pages, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEndpoint();
        ValidatePages(pages);
        if (pdf.Length is < 5 or > 20 * 1024 * 1024 || !pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw new InvalidOperationException("The managed benchmark requires a bounded PDF payload.");
        var path = $"documentintelligence/documentModels/{ModelId}:analyze?api-version={ApiVersion}"
            + $"&outputContentFormat=markdown&stringIndexType=utf16CodeUnit&pages={string.Join(",", pages)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { base64Source = Convert.ToBase64String(pdf) }),
        };
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw new InvalidOperationException($"Managed analysis submission returned HTTP {(int)response.StatusCode}; details withheld.");
        var locations = response.Headers.TryGetValues("Operation-Location", out var values) ? values.ToArray() : [];
        if (locations.Length != 1 || !Uri.TryCreate(locations[0], UriKind.Absolute, out var operation))
            throw new InvalidOperationException("Managed analysis did not return a valid operation receipt; do not resubmit automatically.");
        ValidateOperation(operation);
        return operation;
    }

    /// <summary>Resumes polling an already recorded operation, with no new analysis submission.</summary>
    public async Task<byte[]> PollAsync(Uri operation, int[] pages, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateOperation(operation);
        ValidatePages(pages);
        for (var attempt = 0; attempt < 90; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var response = await httpClient.GetAsync(operation, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Managed analysis polling returned HTTP {(int)response.StatusCode}; resume the receipt instead of resubmitting.");
            var payload = await ReadBoundedAsync(response.Content, cancellationToken);
            using var json = Parse(payload);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("Managed analysis returned no operation status.");
            switch (status.GetString())
            {
                case "succeeded":
                    _ = ValidateResult(payload, pages);
                    return payload;
                case "notStarted":
                case "running":
                    break;
                case "failed":
                case "canceled":
                    throw new InvalidOperationException("Managed analysis did not succeed; private service details withheld.");
                default:
                    throw new InvalidOperationException("Managed analysis returned an unsupported operation status.");
            }
            var delay = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(2));
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 30)), cancellationToken);
        }
        throw new InvalidOperationException("Managed analysis polling budget expired; resume the saved receipt, not the submission.");
    }

    /// <summary>Checks the exact page/model/format contract without treating syntactic success as semantic acceptance.</summary>
    public static Result ValidateResult(byte[] payload, int[] expectedPages)
    {
        ValidatePages(expectedPages);
        using var json = Parse(payload);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !HasString(root, "status", "succeeded")
            || !root.TryGetProperty("analyzeResult", out var result) || result.ValueKind != JsonValueKind.Object
            || !HasString(result, "apiVersion", ApiVersion)
            || !HasString(result, "modelId", ModelId)
            || !HasString(result, "contentFormat", "markdown")
            || !result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String
            || !result.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Managed analysis does not match the requested layout/Markdown contract.");
        var markdown = content.GetString()!;
        if (string.IsNullOrWhiteSpace(markdown))
            throw new InvalidOperationException("Managed analysis returned empty Markdown.");
        var projected = new Dictionary<int, string>();
        foreach (var page in pages.EnumerateArray())
        {
            if (page.ValueKind != JsonValueKind.Object
                || !page.TryGetProperty("pageNumber", out var number) || number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out var pageNumber)
                || !expectedPages.Contains(pageNumber) || projected.ContainsKey(pageNumber)
                || !page.TryGetProperty("spans", out var spans) || spans.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Managed analysis returned unexpected or duplicate pages or missing provenance.");
            var fragments = new List<string>();
            foreach (var span in spans.EnumerateArray())
            {
                if (span.ValueKind != JsonValueKind.Object
                    || !span.TryGetProperty("offset", out var offset) || offset.ValueKind != JsonValueKind.Number || !offset.TryGetInt32(out var start)
                    || !span.TryGetProperty("length", out var length) || length.ValueKind != JsonValueKind.Number || !length.TryGetInt32(out var count)
                    || start < 0 || count < 0 || start > markdown.Length - count)
                    throw new InvalidOperationException("Managed analysis returned invalid UTF-16 page spans.");
                fragments.Add(markdown.Substring(start, count));
            }
            var text = string.Join("\n", fragments);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Managed analysis returned no inspectable text for a requested page.");
            projected.Add(pageNumber, text);
        }
        if (projected.Count != expectedPages.Length)
            throw new InvalidOperationException("Managed analysis omitted requested pages.");
        return new(markdown, projected);
    }

    private void ValidateEndpoint()
    {
        var endpoint = httpClient.BaseAddress;
        if (endpoint is null || endpoint.Scheme != Uri.UriSchemeHttps || endpoint.AbsolutePath != "/"
            || endpoint.Port != 443 || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment) || !string.IsNullOrEmpty(endpoint.UserInfo))
            throw new InvalidOperationException("Configure a credential-free HTTPS Document Intelligence root endpoint.");
    }

    private void ValidateOperation(Uri operation)
    {
        ValidateEndpoint();
        if (!operation.IsAbsoluteUri || operation.GetLeftPart(UriPartial.Authority) != httpClient.BaseAddress!.GetLeftPart(UriPartial.Authority)
            || !string.IsNullOrEmpty(operation.UserInfo) || !string.IsNullOrEmpty(operation.Fragment)
            || !operation.AbsolutePath.StartsWith(ResultPath, StringComparison.Ordinal)
            || !Guid.TryParse(operation.AbsolutePath[ResultPath.Length..], out _)
            || operation.Query != $"?api-version={ApiVersion}")
            throw new InvalidOperationException("Managed analysis operation is outside the approved endpoint/model contract.");
    }

    private static void ValidatePages(int[] pages)
    {
        if (pages.Length is < 1 or > 6 || pages.Any(page => page < 1) || pages.Distinct().Count() != pages.Length)
            throw new InvalidOperationException("The managed benchmark requires one to six distinct positive page numbers.");
    }

    private static bool HasString(JsonElement element, string property, string expected) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == expected;

    private static JsonDocument Parse(byte[] payload)
    {
        if (payload.Length > MaximumResponseBytes)
            throw new InvalidOperationException("Managed analysis response exceeded the byte limit.");
        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Managed analysis returned malformed JSON; content withheld.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaximumResponseBytes)
                throw new InvalidOperationException("Managed analysis response exceeded the byte limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }
}
#endif
