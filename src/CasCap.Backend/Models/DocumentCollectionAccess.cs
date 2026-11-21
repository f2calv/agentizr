namespace CasCap.Models;

/// <summary>Controls how a knowledge collection may be exposed to authenticated callers.</summary>
public enum DocumentCollectionAccess
{
    /// <summary>Uses the existing protected knowledge policy.</summary>
    Private,

    /// <summary>Contains only operator-approved material that does not require individual access grants.</summary>
    PublicReference,
}
