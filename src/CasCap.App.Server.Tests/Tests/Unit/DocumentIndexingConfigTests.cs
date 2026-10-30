#if DEBUG
using System.ComponentModel.DataAnnotations;

namespace CasCap.Tests.Unit;

[Trait("Category", "Indexing")]
public sealed class DocumentIndexingConfigTests
{
    [Fact]
    public void Indexing_DefaultsDisabledWithoutProviderSelection()
    {
        var config = new DocumentIndexingConfig();
        Assert.False(config.Enabled);
        Assert.Empty(config.Profiles);
        Assert.Empty(config.Collections);
        Assert.Empty(Validate(config));
        Assert.Empty(Validate(DocumentIndexingTestData.Config()));
    }

    [Theory]
    [InlineData("enabled-empty")]
    [InlineData("duplicate-profile")]
    [InlineData("duplicate-policy")]
    [InlineData("missing-profile")]
    [InlineData("no-model")]
    [InlineData("no-revision")]
    [InlineData("no-dimensions")]
    [InlineData("metric")]
    [InlineData("no-budget")]
    [InlineData("no-policy-revision")]
    [InlineData("type")]
    [InlineData("cefr")]
    [InlineData("null-profile")]
    [InlineData("null-policy")]
    public void Indexing_InvalidConfigurationFailsValidation(string invalid)
    {
        var config = DocumentIndexingTestData.Config();
        config = invalid switch
        {
            "enabled-empty" => new() { Enabled = true },
            "duplicate-profile" => config with { Profiles = [config.Profiles[0], config.Profiles[0]] },
            "duplicate-policy" => config with { Collections = [config.Collections[0], config.Collections[0]] },
            "missing-profile" => config with { Collections = [config.Collections[0] with { ProfileId = "absent" }] },
            "no-model" => config with { Profiles = [config.Profiles[0] with { Model = string.Empty }] },
            "no-revision" => config with { Profiles = [config.Profiles[0] with { ModelRevision = string.Empty }] },
            "no-dimensions" => config with { Profiles = [config.Profiles[0] with { Dimensions = 0 }] },
            "metric" => config with { Profiles = [config.Profiles[0] with { DistanceMetric = "unknown" }] },
            "no-budget" => config with { Profiles = [config.Profiles[0] with { MaximumTotalTokens = 0 }] },
            "no-policy-revision" => config with { Collections = [config.Collections[0] with { Revision = 0 }] },
            "type" => config with { Collections = [config.Collections[0] with { DocumentType = "Other" }] },
            "cefr" => config with { Collections = [config.Collections[0] with { MaximumCefrLevel = null }] },
            "null-profile" => config with { Profiles = [null!] },
            _ => config with { Collections = [null!] },
        };
        Assert.NotEmpty(Validate(config));
    }

    private static List<ValidationResult> Validate(DocumentIndexingConfig config)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true);
        return results;
    }
}
#endif
