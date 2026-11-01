using CasCap.Exceptions;

namespace CasCap.Tests.Unit;

/// <summary>Checks fail-fast session ownership and deterministic release in Development.</summary>
[Trait("Category", "Agent Runtime")]
public sealed class AgentSessionLockTests
{
    [Fact]
    public async Task AcquireAsync_IsExclusiveAndReleasesOwnership()
    {
        var sessionLock = new InMemoryAgentSessionLock();
        var lease = await sessionLock.AcquireAsync("tenant-a", "assistant", "session", CancellationToken.None);
        await Assert.ThrowsAsync<AgentSessionConflictException>(async () =>
            await sessionLock.AcquireAsync("tenant-a", "assistant", "session", CancellationToken.None));

        await lease.DisposeAsync();
        await using var replacement = await sessionLock.AcquireAsync(
            "tenant-a", "assistant", "session", CancellationToken.None);
        // Disposing an old handle again must not release its replacement.
        await lease.DisposeAsync();
        await Assert.ThrowsAsync<AgentSessionConflictException>(async () =>
            await sessionLock.AcquireAsync("tenant-a", "assistant", "session", CancellationToken.None));
    }

    [Theory]
    [InlineData("tenant-b", "assistant", "session")]
    [InlineData("tenant-a", "other", "session")]
    [InlineData("tenant-a", "assistant", "other")]
    public async Task AcquireAsync_IsolatesResources(string tenantId, string agentName, string sessionId)
    {
        var sessionLock = new InMemoryAgentSessionLock();
        await using var first = await sessionLock.AcquireAsync(
            "tenant-a", "assistant", "session", CancellationToken.None);
        await using var independent = await sessionLock.AcquireAsync(
            tenantId, agentName, sessionId, CancellationToken.None);

        Assert.NotSame(first, independent);
    }

    [Fact]
    public async Task AcquireAsync_CancellationDoesNotTakeOwnership()
    {
        var sessionLock = new InMemoryAgentSessionLock();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await sessionLock.AcquireAsync("tenant-a", "assistant", "session", new CancellationToken(true)));
        await using var lease = await sessionLock.AcquireAsync(
            "tenant-a", "assistant", "session", CancellationToken.None);

        Assert.NotNull(lease);
    }
}
