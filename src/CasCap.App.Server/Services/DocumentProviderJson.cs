#if DEBUG
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Consumes provider JSON with an explicit byte ceiling and payload-free parsing failures.</summary>
internal static class DocumentProviderJson
{
    public static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, int maximumBytes, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException("Document provider response exceeded its byte limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var bytes = new MemoryStream();
        var buffer = new byte[16_384];
        while (bytes.Length <= maximumBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximumBytes + 1L - bytes.Length)), token);
            if (read == 0)
                break;
            await bytes.WriteAsync(buffer.AsMemory(0, read), token);
        }
        if (bytes.Length > maximumBytes)
            throw new InvalidDataException("Document provider response exceeded its byte limit.");
        try
        {
            return JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, (int)bytes.Length));
        }
        catch (JsonException)
        {
            throw new InvalidDataException("Document provider returned invalid JSON.");
        }
    }
}
#endif
