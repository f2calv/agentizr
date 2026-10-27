using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace CasCap.AgentRuntime.Execution.Models;

/// <summary>
/// Per-run state and host callbacks for an agent invocation, replacing the ambient
/// <see cref="System.Threading.AsyncLocal{T}"/> fields that <c>AgentExtensions</c> previously
/// required callers to set and clear in matching pairs.
/// </summary>
/// <remarks>
/// <para>
/// A scope is created by the host, passed to <c>RunAnalysisAsync</c>, and shared with every
/// sub-agent delegation beneath that run. Child scopes created by <see cref="ForSubAgent"/>
/// carry an incremented <see cref="Depth"/> but share the parent's callbacks and attachment
/// collection, so attachments produced deep in a fan-out still surface on the top-level result.
/// </para>
/// <para>
/// TODO: the scope is still propagated to sub-agent tool invocations through a single internal
/// <c>AsyncLocal</c>, because the <c>AIFunction</c> delegate signature offers no parameter channel
/// for it. Investigate flowing it explicitly via <c>AIFunctionArguments.Context</c> /
/// <c>FunctionInvocationContext</c> instead, which would remove the last ambient carrier.
/// See https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.aifunctionarguments
/// </para>
/// </remarks>
public sealed class AgentRunScope
{
    private readonly List<AgentRunAttachment> _attachments;
    private readonly Lock _attachmentsLock;
    // Shared with sub-agents so the budget covers the whole delegation tree.
    private readonly StrongBox<int> _toolInvocations;
    // Per scope, not shared with sub-agents: call IDs are only unique within one model conversation.
    private readonly ConcurrentDictionary<string, bool> _failedToolCalls = new(StringComparer.Ordinal);

    /// <summary>Initializes a new top-level scope.</summary>
    public AgentRunScope()
    {
        _attachments = [];
        _attachmentsLock = new Lock();
        _toolInvocations = new StrongBox<int>();
    }

    private AgentRunScope(AgentRunScope parent, int depth)
    {
        _attachments = parent._attachments;
        _attachmentsLock = parent._attachmentsLock;
        _toolInvocations = parent._toolInvocations;
        OnDelegation = parent.OnDelegation;
        OnCompletion = parent.OnCompletion;
        OnCompaction = parent.OnCompaction;
        OnActivity = parent.OnActivity;
        MaximumToolInvocations = parent.MaximumToolInvocations;
        Depth = depth;
    }

    /// <summary>
    /// Maximum tool invocations, including delegations, across this run and every sub-agent beneath it,
    /// or <see langword="null"/> for no limit.
    /// </summary>
    public int? MaximumToolInvocations { get; init; }

    /// <summary>Gets how many tool invocations the run and its sub-agents have attempted.</summary>
    public int ToolInvocationCount => Volatile.Read(ref _toolInvocations.Value);

    /// <summary>Counts one tool invocation and returns whether it is within <see cref="MaximumToolInvocations"/>.</summary>
    public bool TryConsumeToolInvocation() =>
        Interlocked.Increment(ref _toolInvocations.Value) <= (MaximumToolInvocations ?? int.MaxValue);

    /// <summary>
    /// Nesting depth of the current run: <c>0</c> for the top-level agent, <c>1</c> for a
    /// sub-agent, and so on.
    /// </summary>
    public int Depth { get; private init; }

    /// <summary>
    /// Invoked when a sub-agent delegation begins, with the agent key, nesting depth and the
    /// target agent's provider. Use for live progress notifications.
    /// </summary>
    public Func<string, int, ProviderConfig, CancellationToken, Task>? OnDelegation { get; init; }

    /// <summary>
    /// Invoked when a sub-agent delegation completes, with the agent key, nesting depth and result.
    /// </summary>
    public Func<string, int, AgentRunResult, CancellationToken, Task>? OnCompletion { get; init; }

    /// <summary>Invoked when the chat history is compacted during this run.</summary>
    public Action<CompactionStats>? OnCompaction { get; init; }

    /// <summary>Invoked before/after tool work and for model messages; awaited so a host can persist partial activity.</summary>
    public Func<AgentRunActivity, CancellationToken, ValueTask>? OnActivity { get; init; }

    /// <summary>Records a structured provider receipt before validation; independent of optional conversation history.</summary>
    public Func<AgentStructuredResponse, CancellationToken, ValueTask>? OnStructuredResponse { get; init; }

    /// <summary>Attachments accumulated during this run, including those bubbled up from sub-agents.</summary>
    public IReadOnlyList<AgentRunAttachment> Attachments
    {
        get { lock (_attachmentsLock) return [.. _attachments]; }
    }

    /// <summary>Adds an attachment produced by a tool or sub-agent during this run.</summary>
    /// <param name="attachment">The attachment to record.</param>
    public void AddAttachment(AgentRunAttachment attachment)
    {
        lock (_attachmentsLock)
            _attachments.Add(attachment);
    }

    /// <summary>Removes and returns every attachment accumulated so far.</summary>
    public List<AgentRunAttachment> DrainAttachments()
    {
        lock (_attachmentsLock)
        {
            var drained = new List<AgentRunAttachment>(_attachments);
            _attachments.Clear();
            return drained;
        }
    }

    /// <summary>Creates a child scope for a sub-agent delegation, sharing callbacks and attachments.</summary>
    public AgentRunScope ForSubAgent() => new(this, Depth + 1);

    /// <summary>Records that the tool call with <paramref name="callId"/> threw or returned an error result.</summary>
    /// <param name="callId">The model-assigned call identifier.</param>
    public void MarkToolCallFailed(string callId) => _failedToolCalls[callId] = true;

    /// <summary>Gets whether the tool call with <paramref name="callId"/> was recorded as failed.</summary>
    /// <param name="callId">The model-assigned call identifier.</param>
    public bool IsToolCallFailed(string callId) => _failedToolCalls.ContainsKey(callId);
}
