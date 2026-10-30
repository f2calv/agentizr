#if DEBUG
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Validates native Ollama embedding arrays and usage without network access.</summary>
[Trait("Category", "Indexing")]
public sealed class DocumentOllamaEmbeddingProviderTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("model")]
    [InlineData("count")]
    [InlineData("dimensions")]
    [InlineData("zero")]
    [InlineData("nonfinite")]
    [InlineData("negative-usage")]
    [InlineData("zero-usage")]
    [InlineData("fractional-usage")]
    [InlineData("missing-usage")]
    [InlineData("overflow-usage")]
    public void NativeResponseRequiresExactModelVectorsAndPositiveUsage(string variation)
    {
        var model = "qwen3-embedding:4b";
        var vector = variation switch { "zero" => "[0,0]", "nonfinite" => "[1e100,2]", _ => "[1,2]" };
        var usage = variation switch
        {
            "negative-usage" => "-1",
            "zero-usage" => "0",
            "fractional-usage" => "1.5",
            "overflow-usage" => "1e100",
            _ => "7",
        };
        using var json = JsonDocument.Parse($$"""
            {
                "model": "{{model}}",
                "embeddings": [{{vector}}, [3,4]]
                {{(variation == "missing-usage" ? "" : ", \"prompt_eval_count\": " + usage)}}
            }
            """);
        if (variation == "valid")
        {
            var result = DocumentOllamaEmbeddingProvider.Parse(json.RootElement, model, 2, 2);
            Assert.Equal(7, result.InputTokens);
            Assert.Equal([1f, 2f], result.Vectors[0]);
            Assert.Equal([3f, 4f], result.Vectors[1]);
        }
        else
            Assert.Throws<InvalidDataException>(() => DocumentOllamaEmbeddingProvider.Parse(json.RootElement,
                variation == "model" ? "another:tag" : model, variation == "count" ? 1 : 2, variation == "dimensions" ? 3 : 2));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"model\":null,\"embeddings\":[],\"prompt_eval_count\":1}")]
    [InlineData("{\"model\":\"qwen3-embedding:4b\",\"embeddings\":[null],\"prompt_eval_count\":1}")]
    [InlineData("{\"model\":\"qwen3-embedding:4b\",\"embeddings\":[[\"1\"]],\"prompt_eval_count\":1}")]
    public void InvalidJsonKindsFailExplicitly(string body)
    {
        using var json = JsonDocument.Parse(body);
        Assert.Throws<InvalidDataException>(() => DocumentOllamaEmbeddingProvider.Parse(json.RootElement, "qwen3-embedding:4b", 1, 1));
    }
}
#endif
