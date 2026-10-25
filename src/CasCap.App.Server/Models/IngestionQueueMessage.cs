namespace CasCap.Models;

/// <summary>A transport-neutral, leased event delivery; contents must never enter telemetry.</summary>
internal sealed record IngestionQueueMessage
{
    /// <summary>Gets the opaque transport message identity.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the current opaque acknowledgement/renewal token.</summary>
    public required string LeaseToken { get; init; }

    /// <summary>Gets a bounded notification body.</summary>
    public required BinaryData Body { get; init; }
}
