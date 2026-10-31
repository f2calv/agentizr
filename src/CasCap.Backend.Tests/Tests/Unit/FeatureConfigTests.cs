using CasCap.Constants;

namespace CasCap.Tests.Unit;

/// <summary>Verifies runtime role defaults, normalization and finite-role isolation.</summary>
[Trait("Category", "Roles")]
public sealed class FeatureConfigTests
{
    [Fact]
    public void DefaultSelection() =>
        Assert.Equal([FeatureNames.Agentizr], new FeatureConfig().GetEnabledFeatures());

    [Theory]
    [InlineData("agentizr", 1)]
    [InlineData("ingestion", 1)]
    [InlineData("migrate", 1)]
    [InlineData(" AGENTIZR, ingestion,INGESTION ", 2)]
    public void ValidSelection(string selection, int expectedCount) =>
        Assert.Equal(expectedCount, new FeatureConfig { EnabledFeatures = selection }.GetEnabledFeatures().Count);

    [Theory]
    [InlineData("")]
    [InlineData(" , ")]
    [InlineData("unknown")]
    [InlineData("agentizr,unknown")]
    [InlineData("agentizr,migrate")]
    [InlineData("migrate,ingestion")]
    public void InvalidSelection(string selection) =>
        Assert.Throws<InvalidOperationException>(() => new FeatureConfig { EnabledFeatures = selection }.GetEnabledFeatures());
}
