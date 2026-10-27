namespace CasCap.Entities;

/// <summary>Tenant-qualified page-local extracted text in an immutable prepared generation, without an embedding.</summary>
public sealed class DocumentTextChunkEntity
{
    /// <summary>Gets or sets the owning prepared generation.</summary>
    public required string GenerationId { get; set; }

    /// <summary>Gets or sets the owning tenant.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the owning collection.</summary>
    public required string CollectionId { get; set; }

    /// <summary>Gets or sets the zero-based global chunk ordinal.</summary>
    public int Ordinal { get; set; }

    /// <summary>Gets or sets the one-based source page number.</summary>
    public int PageNumber { get; set; }

    /// <summary>Gets or sets the inclusive UTF-16 offset in the normalized extracted page.</summary>
    public int StartOffset { get; set; }

    /// <summary>Gets or sets the exclusive UTF-16 offset in the normalized extracted page.</summary>
    public int EndOffset { get; set; }

    /// <summary>Gets or sets the actual token count under the captured profile.</summary>
    public int TokenCount { get; set; }

    /// <summary>Gets or sets sensitive extracted text; never include it in telemetry.</summary>
    public required string Text { get; set; }
}
