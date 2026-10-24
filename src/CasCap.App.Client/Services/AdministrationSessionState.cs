namespace CasCap.Client.Services;

/// <summary>Holds the browser's administration session for the lifetime of the downloaded client.</summary>
/// <remarks>WebAssembly scoped services live as long as the page, so one instance serves every component.</remarks>
public sealed class AdministrationSessionState
{
    /// <summary>Gets or sets the session described by the host, or <see langword="null" /> before it is loaded.</summary>
    public AdministrationSessionResponse? Current { get; set; }
}
