using CasCap.Client.Constants;
using CasCap.Client.Models;
using System.Text.Json;

namespace CasCap.TestData;

internal static class DocumentApprovalTestData
{
    public static DocumentApprovalRequest Request(string sequencer = "0001", string etag = "\"example-etag\"") => new()
    {
        Sequencer = sequencer,
        ETag = etag,
        ChunkReviewRevision = 1,
        ExpectedRevision = 0,
        Language = "de",
        DocumentType = DocumentApprovalValues.Vocabulary,
        CefrLevel = "A1",
        ApprovalRequested = true,
        ProvenanceVerified = true,
        ContentVerified = true,
        SafetyVerified = true,
        Notes = "Reviewed against original; no unresolved issues.",
    };

    public static DocumentReviewGenerationResponse Review(bool selected, bool saved, bool current, bool approved) => new()
    {
        GenerationId = "example-generation",
        State = "ReviewRequired",
        Sequencer = "0001",
        ETag = "\"example-etag\"",
        SourceSha256 = new('0', 64),
        Pages = [],
        Projection = JsonSerializer.SerializeToElement(new { }),
        ChunkReview = !selected ? null : new() { Revision = 3, RecordedAtUtc = DateTimeOffset.UnixEpoch, Exclusions = [] },
        Approval = !saved ? null : new()
        {
            Revision = 2,
            RecordedAtUtc = DateTimeOffset.UnixEpoch,
            IsCurrent = current,
            Blockers = approved ? [] : ["Example unresolved gate"],
            Assessment = Request() with { ChunkReviewRevision = 2, ExpectedRevision = 1, Notes = "<script>review</script>" },
        },
    };
}
