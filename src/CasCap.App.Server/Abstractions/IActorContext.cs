namespace CasCap.Abstractions;

/// <summary>Provides the authenticated control-plane actor identifier.</summary>
public interface IActorContext
{
    /// <summary>Gets the stable authenticated actor identifier.</summary>
    string ActorId { get; }
}
