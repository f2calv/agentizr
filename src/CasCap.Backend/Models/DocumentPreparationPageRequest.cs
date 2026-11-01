namespace CasCap.Models;

/// <summary>One derived physical page and its exact managed draft, bound to an immutable tenant-local plan.</summary>
internal sealed record DocumentPreparationPageRequest
{
    /// <summary>Gets the authorized owning tenant.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the admission-time authorization.</summary>
    public required DocumentPreparationPlan Plan { get; init; }

    /// <summary>Gets the original physical page number, not the derived PDF's page index.</summary>
    public required int PageNumber { get; init; }

    /// <summary>Gets a single-page PDF copied without text extraction.</summary>
    public required byte[] Pdf { get; init; }

    /// <summary>Gets the complete original-byte digest.</summary>
    public required string OriginalSha256 { get; init; }

    /// <summary>Gets the same page's managed draft, treated only as untrusted source data.</summary>
    public required string Draft { get; init; }
}
