namespace CasCap.Models;

/// <summary>References an immutable validated receipt; its original digest is not a hash of JSONB's later formatting.</summary>
internal sealed record DocumentMarkdownReceiptReference(string OperationId, string Stage, int PageNumber, string ResultSha256);
