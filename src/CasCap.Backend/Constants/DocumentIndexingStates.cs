namespace CasCap.Constants;

/// <summary>Durable indexing and paid-batch states, independent of preparation and review states.</summary>
internal static class DocumentIndexingStates
{
    public const string Queued = nameof(Queued);
    public const string Processing = nameof(Processing);
    public const string Submitting = nameof(Submitting);
    public const string Received = nameof(Received);
    public const string Published = nameof(Published);
    public const string Revoked = nameof(Revoked);
    public const string Failed = nameof(Failed);
    public const string Conflict = nameof(Conflict);
    // TODO: Add auditable administrator reconciliation with explicit recovery budgets when larger corpora justify it
    // and provider-side operation/usage lookup is available. The pilot uses a separately authorized full replacement;
    // never reset counters or automatically replay an unresolved paid intent.
    public const string ReconciliationRequired = nameof(ReconciliationRequired);
}
