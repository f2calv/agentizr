using CasCap.AgentRuntime.Contracts.V1;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace CasCap.Services;

/// <summary>Maps runtime operations to version 1 DTOs and orchestrates execution streams.</summary>
public sealed class AgentRuntimeApiService(
    AgentExecutionCoordinator coordinator,
    AgentSessionControlService controlSvc)
{
    /// <summary>Gets tenant-scoped agent session status.</summary>
    public async Task<AgentSessionInfoResponse?> GetSessionAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var status = await controlSvc.GetStatusAsync(agentName, sessionId, cancellationToken);
        if (!status.AgentExists)
            return null;
        return new AgentSessionInfoResponse
        {
            Exists = status.SessionExists,
            SessionEnabled = status.SessionEnabled,
            SizeBytes = status.Inspection?.SizeBytes ?? 0,
            Entries = status.Inspection?.Entries.Select(entry => new AgentSessionEntry
            {
                Key = entry.Key,
                ByteSize = entry.ByteSize,
                MessageCount = entry.MessageCount,
                UserMessageCount = entry.UserMessageCount,
                AssistantMessageCount = entry.AssistantMessageCount,
            }).ToArray() ?? [],
        };
    }

    /// <summary>Resets tenant-scoped active agent session state.</summary>
    public ValueTask<bool> ResetSessionAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken) =>
        controlSvc.ResetAsync(agentName, sessionId, cancellationToken);

    /// <summary>Compacts tenant-scoped active agent session history.</summary>
    public async Task<CompactAgentSessionResponse?> CompactSessionAsync(
        string agentName,
        string sessionId,
        CompactAgentSessionRequest request,
        CancellationToken cancellationToken)
    {
        var status = await controlSvc.CompactAsync(
            agentName,
            sessionId,
            request.RetainMessageCount,
            cancellationToken);
        return status.AgentExists
            ? new CompactAgentSessionResponse
            {
                SessionExists = status.SessionExists,
                HistoryAvailable = status.HistoryAvailable,
                RemovedMessageCount = status.RemovedMessageCount,
            }
            : null;
    }

    /// <summary>Saves active agent session state as a named snapshot.</summary>
    public ValueTask<bool> SaveSessionSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken) =>
        controlSvc.SaveSnapshotAsync(agentName, sessionId, snapshotName, cancellationToken);

    /// <summary>Loads a named snapshot into the active agent session.</summary>
    public ValueTask<bool> LoadSessionSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken) =>
        controlSvc.LoadSnapshotAsync(agentName, sessionId, snapshotName, cancellationToken);

    /// <summary>Deletes a named agent session snapshot.</summary>
    public ValueTask<bool> DeleteSessionSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken) =>
        controlSvc.DeleteSnapshotAsync(agentName, sessionId, snapshotName, cancellationToken);

    /// <summary>Gets complete per-session agent runtime overrides.</summary>
    public async Task<AgentOverridesResponse?> GetOverridesAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var overrides = await controlSvc.GetOverridesAsync(agentName, sessionId, cancellationToken);
        return overrides is null
            ? null
            : MapOverrides(overrides);
    }

    /// <summary>Replaces complete per-session agent runtime overrides.</summary>
    public async Task<AgentOverridesResponse?> SetOverridesAsync(
        string agentName,
        string sessionId,
        UpdateAgentOverridesRequest request,
        CancellationToken cancellationToken)
    {
        var overrides = new AgentOverrideState
        {
            SessionEnabled = request.SessionEnabled,
            ModelName = request.ModelName,
            Instructions = request.Instructions,
        };
        return await controlSvc.SetOverridesAsync(agentName, sessionId, overrides, cancellationToken)
            ? MapOverrides(overrides)
            : null;
    }

    private static AgentOverridesResponse MapOverrides(AgentOverrideState overrides) => new()
    {
        SessionEnabled = overrides.SessionEnabled,
        ModelName = overrides.ModelName,
        Instructions = overrides.Instructions,
    };

    /// <summary>Gets effective instructions when the tenant enables disclosure.</summary>
    public async Task<AgentInstructionsResponse?> GetInstructionsAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var view = await controlSvc.GetInstructionsAsync(agentName, sessionId, cancellationToken);
        return view is null
            ? null
            : new AgentInstructionsResponse
            {
                DefinitionVersion = view.DefinitionVersion,
                Overridden = view.Overridden,
                Instructions = view.Instructions,
            };
    }

    /// <summary>Runs one turn against a tenant-scoped agent session.</summary>
    public async Task<RunAgentResponse?> RunAgentAsync(
        string agentName,
        RunAgentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await coordinator.ExecuteAsync(MapRequest(agentName, request), cancellationToken);

        return result is null
            ? null
            : MapResponse(request, result);
    }

    /// <summary>Streams execution events followed by the final tenant-scoped agent response.</summary>
    public async Task<IAsyncEnumerable<RunAgentStreamItem>> StreamAgentAsync(
        string agentName,
        RunAgentRequest request,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<RunAgentStreamItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        var execution = ExecuteStreamAsync(agentName, request, coordinator, channel.Writer, cancellationToken);
        // Surface acquisition failures before JSON streaming commits a successful HTTP status.
        try
        {
            await channel.Reader.WaitToReadAsync(cancellationToken);
        }
        catch
        {
            await execution;
            throw;
        }
        return ReadStreamAsync(channel.Reader, execution, cancellationToken);
    }

    private static async IAsyncEnumerable<RunAgentStreamItem> ReadStreamAsync(
        ChannelReader<RunAgentStreamItem> reader,
        Task execution,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(cancellationToken))
                yield return item;
        }
        finally
        {
            await execution;
        }
    }

    private static async Task ExecuteStreamAsync(
        string agentName,
        RunAgentRequest request,
        AgentExecutionCoordinator coordinator,
        ChannelWriter<RunAgentStreamItem> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            var executionRequest = MapRequest(agentName, request) with
            {
                EventSink = executionEvent => writer.TryWrite(new RunAgentStreamItem
                {
                    Event = MapEvent(executionEvent, request.IncludeDiagnosticDetails),
                }),
            };
            var result = await coordinator.ExecuteAsync(executionRequest, cancellationToken);
            if (result is not null)
                writer.TryWrite(new RunAgentStreamItem { Response = MapResponse(request, result) });
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
    }

    private static AgentExecutionRequest MapRequest(string agentName, RunAgentRequest request) => new()
    {
        AgentName = agentName,
        SessionId = request.SessionId,
        Input = request.Input,
        BinaryContent = request.BinaryContent,
        MimeType = request.MimeType,
        BypassSession = request.BypassSession,
    };

    private static RunAgentResponse MapResponse(RunAgentRequest request, AgentExecutionResult result) => new()
    {
        SessionId = request.SessionId,
        OutputText = result.OutputText,
        DefinitionVersion = result.DefinitionVersion,
        ModelName = result.ModelName,
        FinishReason = result.Diagnostics?.FinishReason,
        ElapsedMilliseconds = result.Diagnostics?.Elapsed.TotalMilliseconds ?? 0,
        TimeToFirstTokenMilliseconds = result.Diagnostics?.TimeToFirstToken?.TotalMilliseconds,
        Usage = result.Diagnostics?.Usage is { } usage ? MapUsage(usage) : null,
        ToolCalls = result.Diagnostics?.ToolCalls
            .Select(toolCall => MapToolCall(toolCall, request.IncludeDiagnosticDetails))
            .ToArray() ?? [],
        Attachments = result.Diagnostics?.Attachments
            .Select(attachment => new RunAgentAttachment
            {
                MimeType = attachment.MimeType,
                FileName = attachment.FileName,
                Base64Content = attachment.Base64Content,
            })
            .ToArray() ?? [],
        Events = result.Events
            .Select(executionEvent => MapEvent(executionEvent, request.IncludeDiagnosticDetails))
            .ToArray(),
        Session = result.SessionInspection is { } inspection
            ? new AgentSessionInfoResponse
            {
                Exists = true,
                SessionEnabled = true,
                SizeBytes = inspection.SizeBytes,
                Entries = inspection.Entries.Select(entry => new AgentSessionEntry
                {
                    Key = entry.Key,
                    ByteSize = entry.ByteSize,
                    MessageCount = entry.MessageCount,
                    UserMessageCount = entry.UserMessageCount,
                    AssistantMessageCount = entry.AssistantMessageCount,
                }).ToArray(),
            }
            : null,
        AdditionalProperties = request.IncludeDiagnosticDetails && result.Diagnostics is { } diagnostics
            ? MapProperties(diagnostics.AdditionalProperties)
            : [],
    };

    private static RunAgentEvent MapEvent(AgentExecutionEvent executionEvent, bool includeDiagnosticDetails) => new()
    {
        Type = executionEvent.Type,
        AgentName = executionEvent.AgentName,
        Depth = executionEvent.Depth,
        ModelName = executionEvent.ModelName,
        ElapsedMilliseconds = executionEvent.Elapsed?.TotalMilliseconds,
        InputMessageCount = executionEvent.InputMessageCount,
        OutputMessageCount = executionEvent.OutputMessageCount,
        ToolMessagesDropped = executionEvent.ToolMessagesDropped,
        WindowMessagesTrimmed = executionEvent.WindowMessagesTrimmed,
        TargetMessageCount = executionEvent.TargetMessageCount,
        Result = executionEvent.Diagnostics is { } diagnostics
            ? new RunAgentStepResult
            {
                ElapsedMilliseconds = diagnostics.Elapsed.TotalMilliseconds,
                Usage = diagnostics.Usage is { } usage ? MapUsage(usage) : null,
                ToolCalls = diagnostics.ToolCalls
                    .Select(toolCall => MapToolCall(toolCall, includeDiagnosticDetails))
                    .ToArray(),
                AdditionalProperties = includeDiagnosticDetails
                    ? MapProperties(diagnostics.AdditionalProperties)
                    : [],
            }
            : null,
    };

    private static RunAgentUsage MapUsage(UsageDetails usage) => new()
    {
        InputTokenCount = usage.InputTokenCount,
        OutputTokenCount = usage.OutputTokenCount,
        TotalTokenCount = usage.TotalTokenCount,
        ReasoningTokenCount = usage.ReasoningTokenCount,
    };

    private static RunAgentToolCall MapToolCall(ToolCallInfo toolCall, bool includeArguments) => new()
    {
        Name = toolCall.Name,
        Arguments = includeArguments && toolCall.Arguments is not null
            ? MapProperties(toolCall.Arguments)
            : [],
        Failed = toolCall.Failed,
    };

    private static Dictionary<string, JsonElement> MapProperties(IEnumerable<KeyValuePair<string, object?>> properties)
    {
        var mapped = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            try
            {
                mapped[key] = JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web);
            }
            catch (NotSupportedException)
            {
                mapped[key] = JsonSerializer.SerializeToElement(value?.ToString(), JsonSerializerOptions.Web);
            }
        }
        return mapped;
    }
}
