using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Validates llama.cpp model receipts using the shared indexed-vector contract without inference.</summary>
[Trait("Category", "Indexing")]
public sealed class DocumentLlamaCppEmbeddingProviderTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("order")]
    [InlineData("model")]
    [InlineData("missing-model")]
    [InlineData("zero-usage")]
    [InlineData("negative-usage")]
    [InlineData("fractional-usage")]
    [InlineData("overflow-usage")]
    [InlineData("missing-usage")]
    [InlineData("total")]
    [InlineData("duplicate")]
    [InlineData("count")]
    [InlineData("dimensions")]
    [InlineData("zero-vector")]
    [InlineData("nonfinite")]
    public void ResponseRequiresExactModelIndexedVectorsAndPositiveNativeUsage(string variation)
    {
        var model = DocumentIndexingTestData.LlamaCppConfig().Profiles[0].Model;
        var usage = variation switch
        {
            "zero-usage" => "0",
            "negative-usage" => "-1",
            "fractional-usage" => "1.5",
            "overflow-usage" => "1e100",
            _ => "7",
        };
        var vector = variation switch { "zero-vector" => "[0,0]", "nonfinite" => "[1e100,2]", _ => "[1,2]" };
        using var json = JsonDocument.Parse($$"""
            {
                {{(variation == "missing-model" ? "" : "\"model\": \"" + (variation == "model" ? "another-model" : model) + "\",")}}
                "data": [
                    {"index": {{(variation == "order" ? 1 : 0)}}, "embedding": {{vector}}},
                    {"index": {{(variation is "order" or "duplicate" ? 0 : 1)}}, "embedding": [3,4]}
                ]
                {{(variation == "missing-usage" ? "" : ", \"usage\": {\"prompt_tokens\": " + usage + ", \"total_tokens\": " + (variation == "total" ? "8" : usage) + "}")}}
            }
            """);
        if (variation is "valid" or "order")
        {
            var result = DocumentLlamaCppEmbeddingProvider.Parse(json.RootElement, model, 2, 2);
            Assert.Equal(7, result.InputTokens);
            Assert.Equal(variation == "order" ? [3f, 4f] : [1f, 2f], result.Vectors[0]);
        }
        else
            Assert.Throws<InvalidDataException>(() => DocumentLlamaCppEmbeddingProvider.Parse(json.RootElement,
                model, variation == "count" ? 1 : 2, variation == "dimensions" ? 3 : 2));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"model\":null}")]
    [InlineData("{\"model\":123}")]
    public void InvalidModelResponseKindsFailExplicitly(string body)
    {
        using var json = JsonDocument.Parse(body);
        Assert.Throws<InvalidDataException>(() => DocumentLlamaCppEmbeddingProvider.Parse(json.RootElement, "model", 1, 1));
    }
}
