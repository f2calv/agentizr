#if DEBUG
namespace CasCap.Models;

/// <summary>Contains only persisted source metadata; synthetic released-lease fields are added after materialization.</summary>
internal sealed record DocumentReviewSource(
    string JobId,
    string Sequencer,
    string PreparationPlanJson,
    string BlobUri,
    string ETag);
#endif
