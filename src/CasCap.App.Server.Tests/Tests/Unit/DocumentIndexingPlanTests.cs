#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using System.Text.Json;

namespace CasCap.Tests.Unit;

[Trait("Category", "Indexing")]
public sealed class DocumentIndexingPlanTests
{
    [Fact]
    public void Indexing_IncludesOnlySavedInputsAndPreservesExactProvenance()
    {
        var review = DocumentIndexingTestData.Review();
        var result = DocumentIndexingPlanBuilder.Build(DocumentIndexingTestData.Config(), review);
        Assert.True(result.Response.Eligible);
        Assert.False(result.Response.IndexingApproved);
        Assert.Empty(result.Response.Blockers);
        var manifest = Assert.IsType<DocumentIndexingManifest>(result.Manifest);
        Assert.Equal(review.Source.TenantId, manifest.TenantId);
        Assert.Equal(review.Source.ETag, manifest.ETag);
        Assert.Equal(review.Snapshot.SourceSha256, manifest.SourceSha256);
        Assert.Equal(review.Snapshot.Projection.Chunks.Where(chunk => chunk.Ordinal != 0),
            manifest.Inputs.Select(input => input.Chunk));
        Assert.All(manifest.Inputs, input => Assert.Equal(
            DocumentPreparationIdentity.Hash(input.Chunk.EmbeddingText), input.InputSha256));
        Assert.Equal(manifest.Inputs.Sum(input => (long)input.Chunk.TokenCount), result.Response.TotalInputTokens);
        Assert.DoesNotContain("notes", JsonSerializer.Serialize(manifest, JsonSerializerOptions.Web), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("embeddingText", JsonSerializer.Serialize(result.Response, JsonSerializerOptions.Web), StringComparison.Ordinal);
        Assert.Equal(result.Response.ManifestSha256,
            DocumentIndexingPlanBuilder.Build(DocumentIndexingTestData.Config(), review).Response.ManifestSha256);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("tenant")]
    [InlineData("collection")]
    [InlineData("manual-policy")]
    [InlineData("language")]
    [InlineData("no-selection")]
    [InlineData("no-approval")]
    [InlineData("stale")]
    [InlineData("withheld")]
    [InlineData("cefr")]
    [InlineData("tokenizer")]
    [InlineData("per-input")]
    [InlineData("total")]
    [InlineData("chunks")]
    public void Indexing_RejectsUnresolvedOrUntrustedInputs(string gate)
    {
        var config = DocumentIndexingTestData.Config();
        var review = DocumentIndexingTestData.Review();
        config = gate switch
        {
            "disabled" => config with { Enabled = false },
            "tenant" => config with { Collections = [config.Collections[0] with { TenantId = "tenant-b" }] },
            "collection" => config with { Collections = [config.Collections[0] with { CollectionId = "other" }] },
            "manual-policy" => config with { Collections = [config.Collections[0] with { DocumentType = DocumentApprovalValues.DeviceManual }] },
            "language" => config with { Collections = [config.Collections[0] with { Language = "en" }] },
            "tokenizer" => config with { Profiles = [config.Profiles[0] with { TokenizerVersion = "other" }] },
            "per-input" => config with { Profiles = [config.Profiles[0] with { MaximumTokensPerInput = 1 }] },
            "total" => config with { Profiles = [config.Profiles[0] with { MaximumTotalTokens = 1 }] },
            "chunks" => config with { Profiles = [config.Profiles[0] with { MaximumChunks = 1 }] },
            _ => config,
        };
        review = gate switch
        {
            "no-selection" => review with { Selection = null },
            "no-approval" => review with { Approval = null },
            "stale" => review with { Selection = review.Selection! with { Revision = 2 } },
            "withheld" => review with { Approval = review.Approval! with { Assessment = review.Approval.Assessment with { ApprovalRequested = false } } },
            "cefr" => review with { Approval = review.Approval! with { Assessment = review.Approval.Assessment with { CefrLevel = "B1" } } },
            _ => review,
        };
        var result = DocumentIndexingPlanBuilder.Build(config, review);
        Assert.False(result.Response.Eligible);
        Assert.False(result.Response.IndexingApproved);
        Assert.NotEmpty(result.Response.Blockers);
        Assert.Null(result.Response.ManifestSha256);
        Assert.Null(result.Manifest);
    }

    [Theory]
    [InlineData("A1", true)]
    [InlineData("A2", true)]
    [InlineData("B1", false)]
    [InlineData("B2", false)]
    [InlineData("C1", false)]
    [InlineData("C2", false)]
    public void Indexing_UsesCumulativeCefrOrder(string level, bool eligible)
    {
        var review = DocumentIndexingTestData.Review();
        review = review with { Approval = review.Approval! with { Assessment = review.Approval.Assessment with { CefrLevel = level } } };
        Assert.Equal(eligible, DocumentIndexingPlanBuilder.Build(DocumentIndexingTestData.Config(), review).Response.Eligible);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("dimensions")]
    [InlineData("policy")]
    [InlineData("assessment")]
    [InlineData("selection")]
    [InlineData("generation")]
    [InlineData("source")]
    public void Indexing_ManifestBindsEveryEligibilityRevision(string change)
    {
        var config = DocumentIndexingTestData.Config();
        var review = DocumentIndexingTestData.Review();
        var original = DocumentIndexingPlanBuilder.Build(config, review).Response.ManifestSha256;
        if (change == "model")
            config = config with { Profiles = [config.Profiles[0] with { ModelRevision = "revision-two" }] };
        if (change == "dimensions")
            config = config with { Profiles = [config.Profiles[0] with { Dimensions = 16 }] };
        if (change == "policy")
            config = config with { Collections = [config.Collections[0] with { MaximumCefrLevel = "B1" }] };
        if (change == "assessment")
            review = review with { Approval = review.Approval! with { Revision = 2 } };
        if (change == "selection")
            review = review with { Selection = review.Selection! with { Exclusions = [] } };
        if (change == "generation")
            review = review with { GenerationId = "another-generation" };
        if (change == "source")
            review = review with { Snapshot = review.Snapshot with { SourceSha256 = new('3', 64) } };
        var changed = DocumentIndexingPlanBuilder.Build(config, review);
        Assert.True(changed.Response.Eligible);
        Assert.NotNull(original);
        Assert.NotEqual(original, changed.Response.ManifestSha256);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("null")]
    public void Indexing_CorruptSelectionFailsExplicitly(string corrupt)
    {
        var review = DocumentIndexingTestData.Review();
        IReadOnlyList<DocumentChunkExclusionRequest> exclusions = corrupt switch
        {
            "unknown" => [new() { Ordinal = int.MaxValue }],
            "duplicate" => [new() { Ordinal = 0 }, new() { Ordinal = 0 }],
            _ => [null!],
        };
        review = review with { Selection = review.Selection! with { Exclusions = exclusions } };
        Assert.Throws<InvalidDataException>(() => DocumentIndexingPlanBuilder.Build(DocumentIndexingTestData.Config(), review));
    }

    [Theory]
    [InlineData("per-input")]
    [InlineData("total")]
    [InlineData("chunks")]
    public void Indexing_InputCeilingsAcceptTheExactLimitAndRejectOneLess(string bound)
    {
        var review = DocumentIndexingTestData.Review();
        var config = DocumentIndexingTestData.Config();
        var inputs = review.Snapshot.Projection.Chunks.Where(chunk => chunk.Ordinal != 0).ToArray();
        var profile = config.Profiles[0] with
        {
            MaximumTokensPerInput = inputs.Max(chunk => chunk.TokenCount),
            MaximumTotalTokens = inputs.Sum(chunk => chunk.TokenCount),
            MaximumChunks = inputs.Length,
        };
        Assert.True(DocumentIndexingPlanBuilder.Build(config with { Profiles = [profile] }, review).Response.Eligible);
        profile = bound switch
        {
            "per-input" => profile with { MaximumTokensPerInput = profile.MaximumTokensPerInput - 1 },
            "total" => profile with { MaximumTotalTokens = profile.MaximumTotalTokens - 1 },
            _ => profile with { MaximumChunks = profile.MaximumChunks - 1 },
        };
        Assert.False(DocumentIndexingPlanBuilder.Build(config with { Profiles = [profile] }, review).Response.Eligible);
    }
}
#endif
