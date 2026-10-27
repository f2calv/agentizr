namespace CasCap.Constants;

/// <summary>Durable paid-operation stages and receipt states; an intent without a receipt never permits another POST.</summary>
internal static class DocumentPreparationOperations
{
    /// <summary>The asynchronous managed layout stage.</summary>
    public const string DocumentIntelligence = nameof(DocumentIntelligence);

    /// <summary>The per-page framework-agent reconciliation stage.</summary>
    public const string PreparationAgent = nameof(PreparationAgent);

    /// <summary>A committed intent whose remote outcome may be unknown.</summary>
    public const string Submitting = nameof(Submitting);

    /// <summary>A managed operation with a durable polling receipt.</summary>
    public const string Polling = nameof(Polling);

    /// <summary>A response captured before local candidate validation.</summary>
    public const string Received = nameof(Received);

    /// <summary>A syntactically valid candidate, not an accepted or searchable generation.</summary>
    public const string Validated = nameof(Validated);

    /// <summary>A known failed or rejected result, retained without automatic paid replay.</summary>
    public const string Rejected = nameof(Rejected);
}
