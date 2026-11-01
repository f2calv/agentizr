using System.Text.Json;

namespace CasCap.AgentRuntime.Execution.Models;

/// <summary>
/// Behavioral configuration for a named AI agent: instructions, prompt, reasoning, tools and prompts.
/// </summary>
/// <remarks>
/// Each <see cref="ToolSource"/> in <see cref="Tools"/> identifies a service class or remote
/// MCP endpoint and optionally filters to a subset of the available tools.
/// Prompts follow the same dual-source pattern via <see cref="PromptSource"/> in <see cref="Prompts"/>.
/// References a <see cref="ProviderConfig"/> by key via <see cref="Provider"/>.
/// Multiple agents are defined in <see cref="AIConfig.Agents"/> keyed by agent name.
/// Agent-specific orchestration settings live in the <see cref="Settings"/> sub-section, bound
/// to a dedicated strongly-typed record.
/// </remarks>
public record AgentConfig
{
    /// <summary>Whether this agent is active. Disabled agents are not registered in DI and are skipped during sub-agent resolution.</summary>
    /// <remarks>Defaults to <c>true</c>.</remarks>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Key into <see cref="AIConfig.Providers"/> identifying the infrastructure provider.
    /// </summary>
    [Required, MinLength(1)]
    public required string Provider { get; init; }

    /// <summary>
    /// System instructions for the agent.
    /// </summary>
    public string? Instructions { get; init; }

    /// <summary>
    /// Optional instruction source: an embedded resource name or an absolute file system path.
    /// </summary>
    /// <remarks>Overrides <see cref="Instructions"/> when set. Resolved by first checking embedded resources, then the file system.</remarks>
    public string? InstructionsSource { get; init; }

    /// <summary>
    /// Display description for the agent.
    /// </summary>
    [Required, MinLength(1)]
    public required string Description { get; init; }

    /// <summary>Display name for the agent.</summary>
    /// <remarks>Also used to derive the Redis session key (<c>agents:sessions:{name}:active</c>).</remarks>
    [Required, MinLength(1)]
    public required string Name { get; init; }

    /// <summary>
    /// The default prompt sent automatically with image/event data.
    /// </summary>
    [Required, MinLength(1)]
    public required string Prompt { get; init; }

    /// <summary>
    /// Tool sources for this agent. Each <see cref="ToolSource"/> identifies either an
    /// in-process service class or a remote MCP endpoint, with optional include/exclude
    /// filters for cherry-picking individual tools across multiple sources.
    /// </summary>
    public ToolSource[] Tools { get; init; } = [];

    /// <summary>
    /// Prompt sources for this agent. Each <see cref="PromptSource"/> identifies either an
    /// in-process prompt type or a remote MCP endpoint, with optional include/exclude
    /// filters for cherry-picking individual prompts across multiple sources.
    /// </summary>
    public PromptSource[] Prompts { get; init; } = [];

    /// <summary>Includes the host's runtime discovery tools and current-time context. Defaults to true.</summary>
    public bool IncludeRuntimeContext { get; init; } = true;

    /// <summary>Disables conversation persistence, session overrides and content-bearing run history.</summary>
    public bool Stateless { get; init; }

    /// <summary>Bounds generated output, including reasoning tokens for Responses models.</summary>
    [Range(1, int.MaxValue)]
    public int? MaximumOutputTokens { get; init; }

    /// <summary>Requests a strict JSON-schema response instead of unstructured text.</summary>
    public JsonOutputConfig? JsonOutput { get; init; }

    /// <summary>
    /// Agent-specific orchestration settings (<see langword="object"/> placeholder for the configuration binder).
    /// </summary>
    /// <remarks>
    /// The JSON sub-tree under this property is bound to a dedicated <see cref="IAppConfig"/>
    /// record via
    /// <c>BindConfiguration($"{nameof(AgentKeys.AIConfig)}:{nameof(AgentKeys.Agents)}:{AgentKeys.Xxx}:{nameof(AgentKeys.Settings)}")</c>.
    /// Consumers should never read this property directly — inject the strongly-typed
    /// <see cref="IOptions{T}"/> record instead.
    /// </remarks>
    public object? Settings { get; init; }
}

/// <summary>Names and describes the JSON object returned by a structured-output agent.</summary>
public sealed record JsonOutputConfig
{
    /// <summary>Gets the schema name sent to the provider.</summary>
    [Required, RegularExpression("^[a-zA-Z0-9_-]{1,64}$")]
    public required string Name { get; init; }

    /// <summary>Gets the provider-supported JSON schema.</summary>
    public required JsonElement Schema { get; init; }
}
