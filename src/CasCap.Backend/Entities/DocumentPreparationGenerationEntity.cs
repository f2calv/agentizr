namespace CasCap.Entities;

/// <summary>Immutable prepared output for one source revision and extraction profile; never a searchable activation.</summary>
public sealed class DocumentPreparationGenerationEntity
{
    /// <summary>Gets or sets the deterministic tenant/source/profile digest.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the owning tenant.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the owning collection.</summary>
    public required string CollectionId { get; set; }

    /// <summary>Gets or sets the tenant-qualified source document.</summary>
    public required string DocumentId { get; set; }

    /// <summary>Gets or sets the admitted source ordering token.</summary>
    public required string Sequencer { get; set; }

    /// <summary>Gets or sets the conditionally downloaded source entity tag.</summary>
    public required string ETag { get; set; }

    /// <summary>Gets or sets the SHA-256 digest of the original PDF bytes.</summary>
    public required string SourceSha256 { get; set; }

    /// <summary>Gets or sets the immutable preparation profile identity.</summary>
    public required string ProfileId { get; set; }

    /// <summary>Gets or sets the complete extraction/tokenizer profile as JSON.</summary>
    public required string ProfileJson { get; set; }

    /// <summary>Gets or sets the PDF page count.</summary>
    public int PageCount { get; set; }

    /// <summary>Gets or sets one-based no-text page numbers as JSON.</summary>
    public required string PagesWithoutTextJson { get; set; }

    /// <summary>Gets or sets the complete chunk count.</summary>
    public int ChunkCount { get; set; }

    /// <summary>Gets or sets when preparation output was staged.</summary>
    public DateTimeOffset PreparedAtUtc { get; set; }
}
