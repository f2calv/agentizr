using ModelContextProtocol.Server;
using System.ComponentModel;

namespace CasCap.Services;

/// <summary>Read-only MCP tools describing the running Agent Runtime.</summary>
/// <remarks>
/// Deliberately returns no definitions, prompts, credentials, provider endpoints, tenant state or
/// session content, and makes no claim about dependency readiness.
/// </remarks>
[McpServerToolType]
public sealed class AgentizrMcpQueryService(ApplicationMetadata applicationMetadata, TimeProvider timeProvider)
{
    /// <summary>Returns the runtime's identity, version and clock.</summary>
    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Gets the Agent Runtime's service name, deployed version, source commit and current UTC time.")]
    public AgentizrMcpStatus GetAgentizrStatus() => new()
    {
        Service = "agentizr",
        Version = applicationMetadata.Version,
        Commit = applicationMetadata.Commit,
        UtcNow = timeProvider.GetUtcNow(),
    };
}
