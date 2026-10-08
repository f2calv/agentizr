using CasCap.AgentRuntime.Contracts.V1;
using CasCap.AgentRuntime.Contracts.V1.Constants;
using CasCap.Constants;
using CasCap.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps version 1 Agent Runtime endpoints.</summary>
public static class AgentRuntimeEndpoints
{
    /// <summary>Maps the version 1 agent execution surface.</summary>
    public static WebApplication MapAgentRuntime(this WebApplication app, bool authorizationRequired)
    {
        var executionGroup = app.MapGroup(AgentRuntimeRoutes.AgentGroup)
            .WithTags("Agent Runtime");
        if (authorizationRequired)
            executionGroup.RequireAuthorization(AgentRuntimePolicies.Execute);

        executionGroup.MapPost(AgentRuntimeRoutes.Runs, RunAgentAsync)
            .WithName("RunAgentV1")
            .WithSummary("Runs one turn against a tenant-scoped agent session")
            .Produces<RunAgentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        executionGroup.MapPost(AgentRuntimeRoutes.RunStream, StreamAgentAsync)
            .WithName("StreamAgentV1")
            .WithSummary("Streams execution events followed by the final tenant-scoped agent response")
            .Produces<IEnumerable<RunAgentStreamItem>>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        executionGroup.MapGet(AgentRuntimeRoutes.Session, GetSessionAsync)
            .WithName("GetAgentSessionV1")
            .WithSummary("Gets tenant-scoped agent session status")
            .Produces<AgentSessionInfoResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        executionGroup.MapDelete(AgentRuntimeRoutes.Session, ResetSessionAsync)
            .WithName("ResetAgentSessionV1")
            .WithSummary("Resets tenant-scoped active agent session state")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        executionGroup.MapPost(AgentRuntimeRoutes.SessionCompaction, CompactSessionAsync)
            .WithName("CompactAgentSessionV1")
            .WithSummary("Compacts tenant-scoped active agent session history")
            .Produces<CompactAgentSessionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        executionGroup.MapPut(AgentRuntimeRoutes.SessionSnapshot, SaveSessionSnapshotAsync)
            .WithName("SaveAgentSessionSnapshotV1")
            .WithSummary("Saves active agent session state as a named snapshot")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        executionGroup.MapPost(AgentRuntimeRoutes.SessionSnapshotActivation, LoadSessionSnapshotAsync)
            .WithName("LoadAgentSessionSnapshotV1")
            .WithSummary("Loads a named snapshot into the active agent session")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        executionGroup.MapDelete(AgentRuntimeRoutes.SessionSnapshot, DeleteSessionSnapshotAsync)
            .WithName("DeleteAgentSessionSnapshotV1")
            .WithSummary("Deletes a named agent session snapshot")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        executionGroup.MapGet(AgentRuntimeRoutes.SessionOverrides, GetOverridesAsync)
            .WithName("GetAgentOverridesV1")
            .WithSummary("Gets complete per-session agent runtime overrides")
            .Produces<AgentOverridesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        executionGroup.MapPut(AgentRuntimeRoutes.SessionOverrides, SetOverridesAsync)
            .WithName("SetAgentOverridesV1")
            .WithSummary("Replaces complete per-session agent runtime overrides")
            .Produces<AgentOverridesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        app.MapGroup(AgentRuntimeRoutes.AgentGroup)
            .WithTags("Agent Definitions")
            .MapAgentDefinitionAdministration(authorizationRequired);

        return app;
    }

    private static async Task<Results<Ok<AgentSessionInfoResponse>, NotFound>> GetSessionAsync(
        string agentName,
        string sessionId,
        AgentSessionControlService controlSvc,
        CancellationToken cancellationToken)
    {
        var status = await controlSvc.GetStatusAsync(agentName, sessionId, cancellationToken);
        if (!status.AgentExists)
            return TypedResults.NotFound();
        return TypedResults.Ok(new AgentSessionInfoResponse
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
        });
    }

    private static async Task<Results<NoContent, NotFound>> ResetSessionAsync(
        string agentName,
        string sessionId,
        AgentSessionControlService controlSvc,
        CancellationToken cancellationToken) =>
        await controlSvc.ResetAsync(agentName, sessionId, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<Results<Ok<CompactAgentSessionResponse>, NotFound>> CompactSessionAsync(
        string agentName,
        string sessionId,
        CompactAgentSessionRequest request,
        AgentSessionControlService controlSvc,
        CancellationToken cancellationToken)
    {
        var status = await controlSvc.CompactAsync(
            agentName,
            sessionId,
            request.RetainMessageCount,
            cancellationToken);
        return status.AgentExists
            ? TypedResults.Ok(new CompactAgentSessionResponse
            {
                SessionExists = status.SessionExists,
                HistoryAvailable = status.HistoryAvailable,
                RemovedMessageCount = status.RemovedMessageCount,
            })
            : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound>> SaveSessionSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        AgentSessionControlService controlSvc,
        CancellationToken cancellationToken) =>
        await controlSvc.SaveSnapshotAsync(agentName, sessionId, snapshotName, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> LoadSessionSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        AgentSessionControlService controlSvc,
        CancellationToken cancellationToken) =>
        await controlSvc.LoadSnapshotAsync(agentName, sessionId, snapshotName, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> DeleteSessionSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        AgentSessionControlService controlSvc,
        CancellationToken cancellationToken) =>
        await controlSvc.DeleteSnapshotAsync(agentName, sessionId, snapshotName, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<Results<Ok<AgentOverridesResponse>, NotFound>> GetOverridesAsync(
        string agentName,
        string sessionId,
        AgentSessionControlService controlSvc,
        CancellationToken cancellationToken)
    {
        var overrides = await controlSvc.GetOverridesAsync(agentName, sessionId, cancellationToken);
        return overrides is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(MapOverrides(overrides));
    }

    private static async Task<Results<Ok<AgentOverridesResponse>, NotFound>> SetOverridesAsync(
        string agentName,
        string sessionId,
        UpdateAgentOverridesRequest request,
        AgentSessionControlService controlSvc,
        CancellationToken cancellationToken)
    {
        var overrides = new AgentOverrideState
        {
            SessionEnabled = request.SessionEnabled,
            ModelName = request.ModelName,
            Instructions = request.Instructions,
        };
        return await controlSvc.SetOverridesAsync(agentName, sessionId, overrides, cancellationToken)
            ? TypedResults.Ok(MapOverrides(overrides))
            : TypedResults.NotFound();
    }

    private static AgentOverridesResponse MapOverrides(AgentOverrideState overrides) => new()
    {
        SessionEnabled = overrides.SessionEnabled,
        ModelName = overrides.ModelName,
        Instructions = overrides.Instructions,
    };

    private static async Task<Results<Ok<RunAgentResponse>, NotFound>> RunAgentAsync(
        string agentName,
        RunAgentRequest request,
        AgentExecutionCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var result = await coordinator.ExecuteAsync(MapRequest(agentName, request), cancellationToken);

        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(MapResponse(request, result));
    }

    private static async IAsyncEnumerable<RunAgentStreamItem> StreamAgentAsync(
        string agentName,
        RunAgentRequest request,
        AgentExecutionCoordinator coordinator,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<RunAgentStreamItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        var execution = ExecuteStreamAsync(agentName, request, coordinator, channel.Writer, cancellationToken);
        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
            yield return item;
        await execution;
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
