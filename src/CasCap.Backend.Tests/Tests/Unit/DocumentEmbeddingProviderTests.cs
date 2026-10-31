#if DEBUG
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Checks exact response shape and vector validity without credentials or network access.</summary>
[Trait("Category", "Indexing")]
public sealed class DocumentEmbeddingProviderTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("order")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("dimensions")]
    [InlineData("zero")]
    [InlineData("usage")]
    [InlineData("total")]
    [InlineData("nonfinite")]
    public void Embedding_RequiresExactIndexedFiniteResultsAndNativeUsage(string variation)
    {
        var json = $$"""
            {
                "data": [
                    { "index": {{(variation == "order" ? 1 : 0)}}, "embedding": {{(variation == "zero" ? "[0,0]" : variation == "nonfinite" ? "[1e100,2]" : "[1,2]")}} }
                    {{(variation == "missing" ? "" : ", { \"index\": " + (variation is "order" or "duplicate" ? "0" : "1") + ", \"embedding\": [3,4] }")}}
                ],
                "usage": { "prompt_tokens": {{(variation == "usage" ? -1 : 7)}}, "total_tokens": {{(variation == "total" ? 8 : 7)}} }
            }
            """;
        using var root = JsonDocument.Parse(json);
        if (variation is "valid" or "order")
        {
            var result = DocumentEmbeddingProvider.Parse(root.RootElement, 2, 2);
            Assert.Equal(7, result.InputTokens);
            Assert.Equal(variation == "order" ? [3f, 4f] : [1f, 2f], result.Vectors[0]);
        }
        else
            Assert.Throws<InvalidDataException>(() => DocumentEmbeddingProvider.Parse(root.RootElement, 2, variation == "dimensions" ? 3 : 2));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProviderResponse_IsByteBoundedEvenWithoutContentLength(bool knownLength)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = knownLength ? new ByteArrayContent(new byte[101]) : new StreamContent(new UnknownLengthStream(new byte[101])),
        };
        if (!knownLength)
            Assert.Null(response.Content.Headers.ContentLength);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            DocumentProviderJson.ReadAsync(response, 100, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"data\":[],\"usage\":null}")]
    [InlineData("{\"data\":[null],\"usage\":{\"prompt_tokens\":1,\"total_tokens\":1}}")]
    [InlineData("{\"data\":[{\"index\":\"0\",\"embedding\":[1]}],\"usage\":{\"prompt_tokens\":1,\"total_tokens\":1}}")]
    public void MalformedResponseKindsAreExplicitValidationFailures(string body)
    {
        using var json = JsonDocument.Parse(body);
        Assert.Throws<InvalidDataException>(() => DocumentEmbeddingProvider.Parse(json.RootElement, 1, 1));
    }

    private sealed class UnknownLengthStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }
}
#endif
