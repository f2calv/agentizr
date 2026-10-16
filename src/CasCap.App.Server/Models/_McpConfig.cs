namespace CasCap.Models;

/// <summary>Opt-in MCP endpoint settings bound from <c>CasCap:McpConfig</c>.</summary>
public sealed record McpConfig : IAppConfig
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(McpConfig)}";

    /// <summary>Whether the read-only status tools are served at <c>/mcp</c>.</summary>
    /// <remarks>Defaults to <see langword="false"/>; read once at startup, so a change needs a restart.</remarks>
    public bool Enabled { get; init; }
}
