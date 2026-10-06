namespace CasCap.Models;

/// <summary>One tenant-scoped request to execute a named agent session.</summary>
public sealed record AgentExecutionRequest
{
    /// <summary>Gets the configured agent name.</summary>
    [Required, MinLength(1)]
    public required string AgentName { get; init; }

    /// <summary>Gets the caller-owned session identifier.</summary>
    [Required, MinLength(1)]
    public required string SessionId { get; init; }

    /// <summary>Gets the user input for this turn.</summary>
    [Required, MinLength(1)]
    public required string Input { get; init; }

    /// <summary>Gets optional binary input.</summary>
    public byte[]? BinaryContent { get; init; }

    /// <summary>Gets the MIME type paired with <see cref="BinaryContent" />.</summary>
    public string? MimeType { get; init; }

    /// <summary>Gets whether session state is bypassed for this turn.</summary>
    public bool BypassSession { get; init; }

    /// <summary>Gets an optional synchronous sink for live execution events.</summary>
    public Action<AgentExecutionEvent>? EventSink { get; init; }
}
