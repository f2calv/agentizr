namespace CasCap.Abstractions;

/// <summary>Identifies the authenticated tenant for the current runtime scope.</summary>
public interface ITenantContext
{
    /// <summary>Gets the stable tenant identifier.</summary>
    string TenantId { get; }
}
