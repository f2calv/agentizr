namespace CasCap.Models;

/// <summary>A validated event bound to a trusted tenant and collection.</summary>
internal sealed record DocumentIngestionNotification
{
    /// <summary>Gets the normalized storage notification.</summary>
    public required BlobStorageEvent Event { get; init; }

    /// <summary>Gets the operator-authorized tenant.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the operator-authorized collection.</summary>
    public required string CollectionId { get; init; }

    /// <summary>Gets the trusted source's explicit hybrid binding, not a value from the storage event.</summary>
    public DocumentAgentPreparationConfig? AgentPreparation { get; init; }
}
