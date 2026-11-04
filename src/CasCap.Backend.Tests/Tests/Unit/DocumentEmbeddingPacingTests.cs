#if DEBUG
using System.Text.Json;

namespace CasCap.Tests.Unit;

[Trait("Category", "Indexing")]
public sealed class DocumentEmbeddingPacingTests
{
    [Theory]
    [InlineData("alias", true)]
    [InlineData("resource-case", true)]
    [InlineData("resource-slash", true)]
    [InlineData("deployment", false)]
    [InlineData("cloud", false)]
    [InlineData("retired", true)]
    [InlineData("ollama", false)]
    public void PacingSharesPhysicalDeploymentNotTenantOrProviderAliases(string variation, bool shared)
    {
        var config = DocumentIndexingTestData.ExecutionConfig();
        var manifest = DocumentIndexingPlanBuilder.Build(config, DocumentIndexingTestData.Review()).Manifest!;
        var azure = manifest.Profile.Azure!;
        var other = variation switch
        {
            "resource-case" => azure with { DeploymentResourceId = azure.DeploymentResourceId.ToUpperInvariant() },
            "resource-slash" => azure with { DeploymentResourceId = azure.DeploymentResourceId + "/" },
            "deployment" => azure with { DeploymentResourceId = azure.DeploymentResourceId + "-other" },
            "cloud" => azure with { ManagementEndpoint = "https://management.example.com/" },
            _ => azure with { Endpoint = "https://alias.example.com/" },
        };
        var previous = manifest with
        {
            TenantId = "other-tenant",
            Profile = manifest.Profile with { Provider = "other-provider", Id = "other-profile", Azure = other },
        };
        if (variation == "ollama")
            previous = previous with { Profile = DocumentIndexingTestData.OllamaConfig().Profiles[0] };
        var json = variation == "retired" ? null : JsonSerializer.Serialize(previous, JsonSerializerOptions.Web);
        Assert.Equal(shared, DocumentEmbeddingPacing.SharesDeployment(azure, json));
    }

    [Theory]
    [InlineData(true, 5, 70)]
    [InlineData(false, 5, 365)]
    [InlineData(false, -1, 65)]
    public void PacingUsesReceiptOrLatestPossibleDispatchNotIntentTime(bool received, int receiptSeconds, int expectedSeconds)
    {
        var submitted = DateTimeOffset.UnixEpoch;
        var next = DocumentEmbeddingPacing.NextSubmissionAt(submitted, received ? submitted.AddSeconds(receiptSeconds) : null,
            receiptSeconds < 0 ? null : submitted.AddSeconds(300), 65);
        Assert.Equal(submitted.AddSeconds(expectedSeconds), next);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    [InlineData(66, false)]
    public void PacingQuietIntervalHasExactBoundary(int elapsedSeconds, bool deferred)
    {
        // The pilot used 9,896 tokens in five seconds; even a tiny next batch must not dispatch immediately.
        var receipt = DateTimeOffset.UnixEpoch.AddSeconds(5);
        var next = DocumentEmbeddingPacing.NextSubmissionAt(DateTimeOffset.UnixEpoch, receipt, null, 65);
        Assert.Equal(deferred, next > receipt.AddSeconds(elapsedSeconds));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PacingRejectsOnlyOversizedAzureBatchesAtPreflight(bool azure)
    {
        var review = DocumentIndexingTestData.Review();
        var config = azure ? DocumentIndexingTestData.ExecutionConfig() : DocumentIndexingTestData.OllamaConfig();
        config = config with { Profiles = [config.Profiles[0] with { MaximumBatchInputs = 2 }] };
        var included = review.Snapshot.Projection.Chunks.Where(chunk => chunk.Ordinal != 0).ToArray();
        var ceiling = included.Chunk(2).Max(batch => batch.Sum(chunk => chunk.TokenCount));
        var exact = DocumentIndexingPlanBuilder.Build(config with { AzureMaximumBatchTokens = ceiling }, review);
        Assert.True(exact.Response.Eligible);
        var over = DocumentIndexingPlanBuilder.Build(config with { AzureMaximumBatchTokens = ceiling - 1 }, review);
        Assert.Equal(!azure, over.Response.Eligible);
        if (azure)
            Assert.Contains(over.Response.Blockers, blocker => blocker.Contains("deployment pacing token ceiling", StringComparison.Ordinal));
    }

    [Fact]
    public void PacingOperationalSettingsDoNotRewriteAuthorizedManifestOrBudgets()
    {
        var config = DocumentIndexingTestData.ExecutionConfig();
        var review = DocumentIndexingTestData.Review();
        var original = DocumentIndexingPlanBuilder.Build(config, review);
        var adjusted = DocumentIndexingPlanBuilder.Build(config with
        {
            AzureMinimumBatchIntervalSeconds = 120,
            AzureMaximumBatchTokens = 8_000,
        }, review);
        Assert.Equal(original.Response.ManifestSha256, adjusted.Response.ManifestSha256);
        Assert.Equal(original.Manifest!.Profile, adjusted.Manifest!.Profile);
        Assert.Equal(original.Response.TotalInputTokens, adjusted.Response.TotalInputTokens);
    }

    [Theory]
    [InlineData("delta", 60)]
    [InlineData("date", 90)]
    [InlineData("past", 0)]
    [InlineData("milliseconds", 71)]
    [InlineData("both", 71)]
    [InlineData("invalid", null)]
    [InlineData("overflow", null)]
    [InlineData("multiple", null)]
    [InlineData("absent", null)]
    public void RetryAfterKeepsOnlyValidatedNumericMetadata(string variation, int? seconds)
    {
        var now = DateTimeOffset.UnixEpoch;
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
        if (variation is "delta" or "both")
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(60));
        if (variation is "date" or "past")
            response.Headers.RetryAfter = new(now.AddSeconds(variation == "past" ? -1 : 90));
        if (variation is "milliseconds" or "both")
            response.Headers.Add("retry-after-ms", "71000");
        if (variation is "invalid" or "overflow" or "multiple")
            response.Headers.TryAddWithoutValidation("retry-after-ms", variation == "multiple" ? ["1", "2"]
                : [variation == "overflow" ? long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture) : "private-response-value"]);
        Assert.Equal(seconds, DocumentEmbeddingProvider.RetryAfter(response, now)?.TotalSeconds);
    }
}
#endif
