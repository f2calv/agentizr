using CasCap.Client.Models;

namespace CasCap.Tests;

/// <summary>Verifies fresh review attestations and immutable observed source/selection binding without I/O.</summary>
[Trait("Category", "DocumentReviewManagement")]
public sealed class DocumentApprovalDraftTests
{
    [Fact]
    public void Draft_ReconfirmsAttestationsAndPinsObservedRevisions()
    {
        var review = DocumentApprovalTestData.Review(true, true, current: false, approved: false);
        var request = new DocumentApprovalDraft(review).Request();
        Assert.Equal("de", request.Language);
        Assert.Equal(3, request.ChunkReviewRevision);
        Assert.Equal(2, request.ExpectedRevision);
        Assert.Equal(review.Sequencer, request.Sequencer);
        Assert.Equal(review.ETag, request.ETag);
        Assert.False(request.ApprovalRequested);
        Assert.False(request.ProvenanceVerified);
        Assert.False(request.ContentVerified);
        Assert.False(request.SafetyVerified);
        Assert.Empty(request.Notes);
        var draft = new DocumentApprovalDraft(review);
        var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateObject(draft,
            new(draft), errors, validateAllProperties: true));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(draft.Notes)));
    }
}
