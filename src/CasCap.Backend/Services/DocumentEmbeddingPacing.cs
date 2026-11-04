#if DEBUG
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Uses durable intents across tenants and replicas; callers hold the ingestion mutation lock.</summary>
internal static class DocumentEmbeddingPacing
{
    public static async Task<bool> IsPendingAsync(
        AgentRuntimeDbContext db, DocumentIndexingConfig config, DocumentIndexingManifest manifest,
        DateTimeOffset now, CancellationToken token)
    {
        if (manifest.Profile.Azure is not { } azure)
            return false;
        var cutoff = now.AddSeconds(-config.AzureMinimumBatchIntervalSeconds);
        var recent = await db.DocumentEmbeddingBatches.AsNoTracking()
            .Join(db.DocumentIndexingJobs.AsNoTracking(),
                batch => new { batch.TenantId, Id = batch.JobId },
                job => new { job.TenantId, job.Id }, (batch, job) => new
                {
                    job.ManifestJson,
                    batch.SubmittedAtUtc,
                    batch.ReceivedAtUtc,
                    job.ClaimExpiresAtUtc,
                })
            .Where(item => item.SubmittedAtUtc > cutoff || item.ReceivedAtUtc > cutoff || item.ClaimExpiresAtUtc > cutoff)
            .ToArrayAsync(token);
        return recent.Any(item => NextSubmissionAt(item.SubmittedAtUtc, item.ReceivedAtUtc, item.ClaimExpiresAtUtc,
                config.AzureMinimumBatchIntervalSeconds) > now
            && SharesDeployment(azure, item.ManifestJson));
    }

    internal static DateTimeOffset NextSubmissionAt(
        DateTimeOffset submitted, DateTimeOffset? received, DateTimeOffset? claimExpires, int intervalSeconds)
    {
        var anchor = received ?? claimExpires ?? submitted;
        return (anchor > submitted ? anchor : submitted).AddSeconds(intervalSeconds);
    }

    internal static bool SharesDeployment(DocumentAzureEmbeddingConfig azure, string? manifestJson)
    {
        // Retirement erases private manifests, not intents: an unidentifiable recent intent gates all Azure deployments.
        if (manifestJson is null)
            return true;
        using var json = JsonDocument.Parse(manifestJson);
        var binding = json.RootElement.GetProperty("profile").GetProperty("azure");
        return binding.ValueKind != JsonValueKind.Null
            && string.Equals(azure.DeploymentResourceId.TrimEnd('/'),
                binding.GetProperty("deploymentResourceId").GetString()?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            && new Uri(azure.ManagementEndpoint) == new Uri(binding.GetProperty("managementEndpoint").GetString()!);
    }
}
#endif
