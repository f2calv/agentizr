namespace CasCap.Entities;

/// <summary>Durable version-qualified session, snapshot or override payload.</summary>
public sealed class AgentStateEntity
{
    /// <summary>Gets or sets the opaque tenant-qualified state key.</summary>
    public required string Key { get; set; }

    /// <summary>Gets or sets the tenant owning this state.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the agent owning this state.</summary>
    public required string AgentName { get; set; }

    /// <summary>Gets or sets the caller's session identifier.</summary>
    public required string SessionId { get; set; }

    /// <summary>Gets or sets the immutable payload revision used for caching.</summary>
    public Guid Revision { get; set; }

    /// <summary>Gets or sets the serialized payload.</summary>
    public required string PayloadJson { get; set; }

    /// <summary>Gets or sets the last durable write time.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
