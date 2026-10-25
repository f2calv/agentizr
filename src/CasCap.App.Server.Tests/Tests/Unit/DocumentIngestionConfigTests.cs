#if DEBUG
using System.ComponentModel.DataAnnotations;

namespace CasCap.Tests.Unit;

/// <summary>Verifies ingestion configuration without resolving external dependencies.</summary>
[Trait("Category", "Ingestion")]
public sealed class DocumentIngestionConfigTests
{
    [Fact]
    public void ValidConfiguration() => Assert.Empty(Validate(DocumentIngestionTestData.Config));

    [Theory]
    [InlineData("http://queue.example.com/events")]
    [InlineData("https://queue.example.com/events?sig=invalid")]
    [InlineData("https://queue.example.com/events/nested")]
    [InlineData("https://queue.example.com/poison/")]
    public void InvalidQueueEndpoint(string endpoint) =>
        Assert.NotEmpty(Validate(DocumentIngestionTestData.Config with { QueueUri = endpoint }));

    [Fact]
    public void DuplicateContainer()
    {
        var config = DocumentIngestionTestData.Config;
        Assert.NotEmpty(Validate(config with { Sources = [config.Sources[0], config.Sources[0]] }));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(601)]
    public void InvalidVisibility(int seconds) =>
        Assert.NotEmpty(Validate(DocumentIngestionTestData.Config with { VisibilitySeconds = seconds }));

    private static List<ValidationResult> Validate(DocumentIngestionConfig config)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true);
        return results;
    }
}
#endif
