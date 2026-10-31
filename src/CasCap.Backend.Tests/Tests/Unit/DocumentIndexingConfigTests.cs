#if DEBUG
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CasCap.Tests.Unit;

[Trait("Category", "Indexing")]
public sealed class DocumentIndexingConfigTests
{
    [Fact]
    public void Indexing_DefaultsDisabledWithoutProviderSelection()
    {
        var config = new DocumentIndexingConfig();
        Assert.False(config.Enabled);
        Assert.False(config.ProcessingEnabled);
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
    [InlineData("processing-disabled")]
    [InlineData("processing-no-azure")]
    [InlineData("no-batch")]
    [InlineData("invalid-azure")]
    [InlineData("management-authority")]
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
            "null-policy" => config with { Collections = [null!] },
            "processing-disabled" => config with { Enabled = false, ProcessingEnabled = true },
            "processing-no-azure" => config with { ProcessingEnabled = true },
            "no-batch" => config with { Profiles = [config.Profiles[0] with { MaximumBatchInputs = 0 }] },
            "invalid-azure" => config with { Profiles = [config.Profiles[0] with { Azure = new() }] },
            _ => config with { Profiles = [config.Profiles[0] with { Azure = DocumentIndexingTestData.Azure with { ManagementEndpoint = "https://untrusted.example.com/" } }] },
        };
        Assert.NotEmpty(Validate(config));
    }

    private static List<ValidationResult> Validate(DocumentIndexingConfig config)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("both")]
    [InlineData("digest")]
    [InlineData("null-digest")]
    [InlineData("tag")]
    [InlineData("null-model")]
    [InlineData("scheme")]
    [InlineData("credentials")]
    [InlineData("path")]
    [InlineData("query")]
    [InlineData("fragment")]
    public void OllamaRequiresOneExplicitBindingTagAndDigest(string variation)
    {
        var config = DocumentIndexingTestData.OllamaConfig();
        var profile = config.Profiles[0];
        profile = variation switch
        {
            "both" => profile with { Azure = DocumentIndexingTestData.Azure },
            "digest" => profile with { ModelRevision = "short-digest" },
            "null-digest" => profile with { ModelRevision = null! },
            "tag" => profile with { Model = "qwen3-embedding" },
            "null-model" => profile with { Model = null! },
            "scheme" => profile with { Ollama = new() { Endpoint = "ftp://ollama.example.com/" } },
            "credentials" => profile with { Ollama = new() { Endpoint = "http://user:password@ollama.example.com/" } },
            "path" => profile with { Ollama = new() { Endpoint = "http://ollama.example.com/api/" } },
            "query" => profile with { Ollama = new() { Endpoint = "http://ollama.example.com/?option=1" } },
            "fragment" => profile with { Ollama = new() { Endpoint = "http://ollama.example.com/#fragment" } },
            _ => profile,
        };
        config = config with { Profiles = [profile] };
        if (variation == "valid")
        {
            Assert.Empty(Validate(config));
            Assert.True(profile.HasExecutionBinding);
            Assert.True(DocumentIndexingPlanBuilder.Build(config, DocumentIndexingTestData.Review()).Response.Eligible);
        }
        else
            Assert.NotEmpty(Validate(config));
    }

    [Fact]
    public void OllamaBindingDoesNotAlterAzureSerializationOrBypassManualGates()
    {
        var azure = DocumentIndexingTestData.ExecutionConfig().Profiles[0];
        Assert.True(azure.HasExecutionBinding);
        Assert.DoesNotContain("\"ollama\":", JsonSerializer.Serialize(azure, JsonSerializerOptions.Web), StringComparison.Ordinal);
        var ollama = DocumentIndexingTestData.OllamaConfig();
        ollama = ollama with { Collections = [ollama.Collections[0] with { DocumentType = CasCap.Client.Constants.DocumentApprovalValues.DeviceManual }] };
        var preflight = DocumentIndexingPlanBuilder.Build(ollama, DocumentIndexingTestData.Review());
        Assert.False(preflight.Response.Eligible);
        Assert.Contains(preflight.Response.Blockers, blocker => blocker.Contains("verified visual associations", StringComparison.Ordinal));
    }
}
#endif
