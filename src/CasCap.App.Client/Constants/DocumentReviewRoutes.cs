namespace CasCap.Client.Constants;

/// <summary>Same-origin review-management routes, distinct from machine execution and content approval.</summary>
public static class DocumentReviewRoutes
{
    /// <summary>Gets the current-source preview route.</summary>
    public const string Source = AdministrationRoutes.App + "/collections/{collectionId}/documents/{documentId}/jobs/{jobId}/review";

    /// <summary>Gets the explicit review-generation storage route.</summary>
    public const string Generations = Source + "/generations";

    /// <summary>Gets one exact immutable generation.</summary>
    public const string Generation = Generations + "/{generationId}";

    /// <summary>Gets the optimistic-concurrency-protected selection review route for an exact snapshot.</summary>
    public const string ChunkReview = Generation + "/chunk-review";

    /// <summary>Escapes all caller-supplied route values.</summary>
    public static string Format(string template, string appId, string collectionId, string documentId, string jobId, string? generationId = null) =>
        AdministrationRoutes.Format(template, appId)
            .Replace("{collectionId}", Uri.EscapeDataString(collectionId), StringComparison.Ordinal)
            .Replace("{documentId}", Uri.EscapeDataString(documentId), StringComparison.Ordinal)
            .Replace("{jobId}", Uri.EscapeDataString(jobId), StringComparison.Ordinal)
            .Replace("{generationId}", generationId is null ? string.Empty : Uri.EscapeDataString(generationId), StringComparison.Ordinal);
}
