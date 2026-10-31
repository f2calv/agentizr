#if DEBUG
using System.ComponentModel.DataAnnotations;

namespace CasCap.Tests.Unit;

/// <summary>Validates preparation defaults and nested per-call chunk-budget constraints.</summary>
[Trait("Category", "PdfPreparation")]
public sealed class DocumentPreparationConfigTests
{
    [Fact]
    public void Defaults()
    {
        var config = new DocumentPreparationConfig();
        Assert.Empty(Validate(config));
        Assert.Equal(25 * 1024 * 1024, config.MaximumPdfBytes);
        Assert.Equal(200, config.MaximumPages);
        Assert.Equal(2_000_000, config.MaximumExtractedCharacters);
        Assert.Equal(5_000, config.MaximumChunks);
        Assert.Equal(512, config.Chunking.MaximumTokensPerChunk);
        Assert.Equal(64, config.Chunking.OverlapTokens);
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 1, 0)]
    public void InvalidResourceLimit(int bytes, int pages, int characters, int chunks) =>
        Assert.NotEmpty(Validate(new DocumentPreparationConfig
        {
            MaximumPdfBytes = bytes,
            MaximumPages = pages,
            MaximumExtractedCharacters = characters,
            MaximumChunks = chunks,
        }));

    [Fact]
    public void NestedChunkingIsValidated() =>
        Assert.NotEmpty(Validate(new DocumentPreparationConfig
        {
            Chunking = new DocumentChunkingConfig { MaximumTokensPerChunk = 32, OverlapTokens = 32 },
        }));

    private static List<ValidationResult> Validate(DocumentPreparationConfig config)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true);
        return results;
    }
}
#endif
