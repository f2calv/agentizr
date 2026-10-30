#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Derives indexing eligibility from trusted configuration and exact saved review inputs, without side effects.</summary>
internal static class DocumentIndexingPlanBuilder
{
    /// <summary>Returns explicit blockers or a content-addressed input manifest; never grants permission to call a provider.</summary>
    public static DocumentIndexingPreflight Build(DocumentIndexingConfig config, DocumentIndexingReview review)
    {
        Validator.ValidateObject(config, new ValidationContext(config), validateAllProperties: true);
        var blockers = new List<string>();
        var source = review.Source;
        var chunks = review.Snapshot.Projection.Chunks;
        var candidates = chunks.Select(chunk => chunk.Ordinal).ToHashSet();
        if (candidates.Count != chunks.Count || candidates.Any(ordinal => ordinal < 0))
            throw new InvalidDataException("Indexing candidates have invalid or duplicate ordinals.");
        var exclusions = review.Selection?.Exclusions ?? [];
        if (exclusions.Any(item => item is null || !candidates.Contains(item.Ordinal))
            || exclusions.Select(item => item.Ordinal).Distinct().Count() != exclusions.Count)
            throw new InvalidDataException("The saved indexing selection does not match the snapshot.");
        var excluded = exclusions.Select(item => item.Ordinal).ToHashSet();
        var inputs = chunks.Where(chunk => !excluded.Contains(chunk.Ordinal)).OrderBy(chunk => chunk.Ordinal).ToArray();
        if (inputs.Any(chunk => string.IsNullOrWhiteSpace(chunk.EmbeddingText) || chunk.TokenCount <= 0))
            throw new InvalidDataException("An included candidate has no valid embedding input.");
        var total = inputs.Sum(chunk => (long)chunk.TokenCount);
        if (!config.Enabled)
            blockers.Add("Indexing preflight is disabled.");
        if (review.Selection is null)
            blockers.Add("A saved chunk selection is required.");
        if (review.Approval is null)
            blockers.Add("A current document readiness assessment is required.");
        else
        {
            var assessment = review.Approval.Assessment;
            Validator.ValidateObject(assessment, new ValidationContext(assessment), validateAllProperties: true);
            if (assessment.Sequencer != source.Sequencer || assessment.ETag != source.ETag)
                throw new InvalidDataException("The saved assessment source binding is inconsistent.");
            if (!review.Approval.IsCurrent || assessment.ChunkReviewRevision != review.Selection?.Revision)
                blockers.Add("The saved selection changed; reassessment is required.");
            blockers.AddRange(assessment.Blockers(inputs.Length));
        }
        var policy = config.Collections.SingleOrDefault(item =>
            item.TenantId == source.TenantId && item.CollectionId == source.CollectionId);
        var profile = policy is null ? null : config.Profiles.Single(item => item.Id == policy.ProfileId);
        if (policy is null)
            blockers.Add("No trusted indexing policy is configured for this collection.");
        else if (review.Approval is { Assessment: var assessment })
        {
            if (assessment.DocumentType != policy.DocumentType || assessment.Language != policy.Language)
                blockers.Add("Reviewed classification or language does not match trusted collection policy.");
            if (policy.DocumentType == DocumentApprovalValues.DeviceManual)
                blockers.Add("Trusted manual collections require verified visual associations; indexing remains blocked.");
            if (policy.MaximumCefrLevel is { } maximum && (assessment.CefrLevel is not { } level
                || !DocumentApprovalValues.CefrLevels.TakeWhile(value => value != maximum).Append(maximum).Contains(level, StringComparer.Ordinal)))
                blockers.Add("Reviewed content exceeds the collection CEFR limit or has no reviewed level.");
        }
        if (profile is not null)
        {
            var tokenizer = review.Snapshot.Projection.Profile;
            if (profile.TokenizerEncoding != tokenizer.TokenizerEncoding || profile.TokenizerVersion != tokenizer.TokenizerVersion)
                blockers.Add("Embedding profile and snapshot tokenizer bindings are incompatible.");
            if (inputs.Length > profile.MaximumChunks || total > profile.MaximumTotalTokens
                || inputs.Any(chunk => chunk.TokenCount > profile.MaximumTokensPerInput))
                blockers.Add("Included embedding inputs exceed the configured chunk or token bounds.");
        }
        DocumentIndexingManifest? manifest = blockers.Count != 0 ? null : new()
        {
            TenantId = source.TenantId,
            CollectionId = source.CollectionId,
            DocumentId = source.DocumentId,
            JobId = source.JobId,
            Sequencer = source.Sequencer,
            ETag = source.ETag!,
            SourceSha256 = review.Snapshot.SourceSha256,
            GenerationId = review.GenerationId,
            ChunkReviewRevision = review.Selection!.Revision,
            AssessmentRevision = review.Approval!.Revision,
            Policy = policy!,
            Profile = profile!,
            Inputs = inputs.Select(chunk => new DocumentIndexingInput(chunk, DocumentPreparationIdentity.Hash(chunk.EmbeddingText))).ToArray(),
        };
        return new(new()
        {
            Eligible = manifest is not null,
            Blockers = blockers.Distinct(StringComparer.Ordinal).ToArray(),
            ManifestSha256 = manifest is null ? null : DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(manifest, JsonSerializerOptions.Web)),
            ChunkReviewRevision = review.Selection?.Revision,
            AssessmentRevision = review.Approval?.Revision,
            IncludedChunkCount = review.Selection is null ? 0 : inputs.Length,
            TotalInputTokens = review.Selection is null ? 0 : total,
        }, manifest);
    }
}
#endif
