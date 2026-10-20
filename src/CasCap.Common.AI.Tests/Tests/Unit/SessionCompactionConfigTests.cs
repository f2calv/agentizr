namespace CasCap.Common.AI.Tests.Unit;

/// <summary>Tests which compaction passes a <see cref="SessionCompactionConfig"/> reports.</summary>
[Trait("Category", "Chat Reduction")]
public sealed class SessionCompactionConfigTests
{
    [Theory]
    [InlineData(CompactionNotificationMode.Off, 0, false)]
    [InlineData(CompactionNotificationMode.Off, 3, false)]
    [InlineData(CompactionNotificationMode.WindowTrimmed, 0, false)]
    [InlineData(CompactionNotificationMode.WindowTrimmed, 3, true)]
    [InlineData(CompactionNotificationMode.Always, 0, true)]
    [InlineData(CompactionNotificationMode.Always, 3, true)]
    public void ShouldNotify_FollowsMode(CompactionNotificationMode mode, int windowTrimmed, bool expected)
    {
        var config = new SessionCompactionConfig { Notification = mode };

        Assert.Equal(expected, config.ShouldNotify(new CompactionStats(8, 5, 1, windowTrimmed, 4)));
    }
}
