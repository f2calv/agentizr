namespace CasCap.Services;

/// <summary>Provides a synthetic actor for Development when JWT authentication is disabled.</summary>
internal sealed class ConfiguredActorContext : IActorContext
{
    /// <inheritdoc/>
    public string ActorId => "development";
}
