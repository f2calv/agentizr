using System.Collections.Concurrent;

namespace CasCap.AgentRuntime.Evaluation.Services;

/// <summary>Collects tool calls made during a single definition evaluation run.</summary>
public sealed class EvaluationRecorder
{
    private readonly ConcurrentQueue<RecordedToolCall> _toolCalls = new();

    /// <summary>Tool calls in the order they were made.</summary>
    public IReadOnlyList<RecordedToolCall> ToolCalls => [.. _toolCalls];

    /// <summary>Records a tool call.</summary>
    /// <param name="toolCall">The call to record.</param>
    public void Add(RecordedToolCall toolCall) => _toolCalls.Enqueue(toolCall);
}
