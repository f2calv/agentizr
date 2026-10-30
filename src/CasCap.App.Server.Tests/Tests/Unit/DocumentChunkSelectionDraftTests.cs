using CasCap.Client.Models;

namespace CasCap.Tests;

/// <summary>Proves local inclusion decisions without browser, storage or provider execution.</summary>
[Trait("Category", "DocumentReviewManagement")]
public sealed class DocumentChunkSelectionDraftTests
{
    [Fact]
    public void Selection_DefaultsIncludesEveryCandidateAndTracksEdits()
    {
        var draft = new DocumentChunkSelectionDraft([0, 1, 2], null);
        Assert.Equal(3, draft.IncludedCount);
        Assert.Equal(0, draft.ExcludedCount);
        Assert.False(draft.IsDirty);
        draft.SetIncluded(1, false);
        draft.SetReason(1, "irrelevant");
        Assert.True(draft.IsDirty);
        Assert.Equal(2, draft.IncludedCount);
        Assert.Equal(1, draft.ExcludedCount);
        Assert.True(draft.IsExcluded(1));
        Assert.Equal("irrelevant", Assert.Single(draft.Exclusions()).Reason);
        draft.SetIncluded(1, true);
        Assert.False(draft.IsDirty);
        Assert.Null(draft.Reason(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.SetIncluded(3, false));
        Assert.Throws<InvalidOperationException>(() => draft.SetReason(0, "not excluded"));
    }

    [Fact]
    public void Selection_LoadsSavedBaselineAndAllowsAllExcluded()
    {
        var draft = new DocumentChunkSelectionDraft([0, 1], new()
        {
            Revision = 2,
            Exclusions = [new() { Ordinal = 1, Reason = "duplicate" }],
            RecordedAtUtc = DateTimeOffset.UnixEpoch,
        });
        Assert.False(draft.IsDirty);
        Assert.Equal(1, draft.IncludedCount);
        draft.SetIncluded(0, false);
        Assert.Equal(0, draft.IncludedCount);
        Assert.Equal([0, 1], draft.Exclusions().Select(item => item.Ordinal));
        draft.SetIncluded(0, true);
        draft.SetReason(1, "incorrect");
        Assert.True(draft.IsDirty);
        draft.SetReason(1, "duplicate");
        Assert.False(draft.IsDirty);
    }
}
