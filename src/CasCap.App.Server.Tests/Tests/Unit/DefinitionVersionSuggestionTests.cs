using CasCap.Client.Models;
using System.Globalization;

namespace CasCap.Tests.Unit;

/// <summary>Verifies automatic editor version suggestions without a clock or locale dependency.</summary>
[Trait("Category", "Generation")]
public sealed class DefinitionVersionSuggestionTests
{
    [Theory]
    [InlineData("", "2030-01-02.1")]
    [InlineData("2029-12-31.1|v1", "2030-01-02.1")]
    [InlineData("2030-01-02.1", "2030-01-02.2")]
    [InlineData("2030-01-02.3|2030-01-02.1|2030-01-02.2", "2030-01-02.4")]
    [InlineData("2030-01-02.1|2030-01-02.3", "2030-01-02.2")]
    [InlineData("2030-01-02.01|2030-01-02.1-custom", "2030-01-02.1")]
    public void SuggestVersion(string versions, string expected)
    {
        // Cloning a version must prepopulate an unused identifier rather than leave it blank.
        var existing = versions.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var clock = new FixedTimeProvider(new DateTimeOffset(2030, 1, 2, 0, 30, 0, TimeSpan.Zero));

        var suggested = DefinitionDraft.SuggestVersion(existing, clock);

        Assert.Equal(expected, suggested);
        Assert.DoesNotContain(suggested, existing);
        Assert.InRange(suggested.Length, 1, 100);
    }

    [Fact]
    public void SuggestVersion_UsesInvariantUtcDate()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var clock = new FixedTimeProvider(new DateTimeOffset(2030, 1, 2, 23, 30, 0, TimeSpan.FromHours(-2)));

            Assert.Equal("2030-01-03.1", DefinitionDraft.SuggestVersion([], clock));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
