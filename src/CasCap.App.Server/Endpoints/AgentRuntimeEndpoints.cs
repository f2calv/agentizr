using CasCap.AgentRuntime.Contracts.V1;
using CasCap.AgentRuntime.Contracts.V1.Constants;
using CasCap.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps version 1 Agent Runtime endpoints.</summary>
public static class AgentRuntimeEndpoints
{
    /// <summary>Maps the version 1 agent execution surface.</summary>
    public static WebApplication MapAgentRuntime(this WebApplication app, bool authorizationRequired)
    {
        var group = app.MapGroup(AgentRuntimeRoutes.AgentGroup)
            .WithTags("Agent Runtime");
        if (authorizationRequired)
            group.RequireAuthorization();

        group.MapPost(AgentRuntimeRoutes.Runs, RunAgentAsync)
            .WithName("RunAgentV1")
            .WithSummary("Runs one turn against a tenant-scoped agent session")
            .Produces<RunAgentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        group.MapGet(AgentRuntimeRoutes.Session, GetSessionAsync)
            .WithName("GetAgentSessionV1")
            .WithSummary("Gets tenant-scoped agent session status")
            .Produces<AgentSessionInfoResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete(AgentRuntimeRoutes.Session, ResetSessionAsync)
            .WithName("ResetAgentSessionV1")
            .WithSummary("Resets tenant-scoped active agent session state")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost(AgentRuntimeRoutes.SessionCompaction, CompactSessionAsync)
            .WithName("CompactAgentSessionV1")
            .WithSummary("Compacts tenant-scoped active agent session history")
            .Produces<CompactAgentSessionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        group.MapPut(AgentRuntimeRoutes.SessionSnapshot, SaveSessionSnapshotAsync)
            .WithName("SaveAgentSessionSnapshotV1")
            .WithSummary("Saves active agent session state as a named snapshot")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost(AgentRuntimeRoutes.SessionSnapshotActivation, LoadSessionSnapshotAsync)
            .WithName("LoadAgentSessionSnapshotV1")
            .WithSummary("Loads a named snapshot into the active agent session")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete(AgentRuntimeRoutes.SessionSnapshot, DeleteSessionSnapshotAsync)
            .WithName("DeleteAgentSessionSnapshotV1")
            .WithSummary("Deletes a named agent session snapshot")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet(AgentRuntimeRoutes.SessionOverrides, GetOverridesAsync)
            .WithName("GetAgentOverridesV1")
            .WithSummary("Gets complete per-session agent runtime overrides")
            .Produces<AgentOverridesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut(AgentRuntimeRoutes.SessionOverrides, SetOverridesAsync)
            .WithName("SetAgentOverridesV1")
            .WithSummary("Replaces complete per-session agent runtime overrides")
            .Produces<AgentOverridesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

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
        var result = await coordinator.ExecuteAsync(new AgentExecutionRequest
        {
            AgentName = agentName,
            SessionId = request.SessionId,
            Input = request.Input,
            BinaryContent = request.BinaryContent,
            MimeType = request.MimeType,
            BypassSession = request.BypassSession,
        }, cancellationToken);

        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new RunAgentResponse
            {
                SessionId = request.SessionId,
                OutputText = result.OutputText,
                DefinitionVersion = result.DefinitionVersion,
                ModelName = result.ModelName,
                FinishReason = result.Diagnostics?.FinishReason,
                ElapsedMilliseconds = result.Diagnostics?.Elapsed.TotalMilliseconds ?? 0,
                TimeToFirstTokenMilliseconds = result.Diagnostics?.TimeToFirstToken?.TotalMilliseconds,
                Usage = result.Diagnostics?.Usage is { } usage
                    ? new RunAgentUsage
                    {
                        InputTokenCount = usage.InputTokenCount,
                        OutputTokenCount = usage.OutputTokenCount,
                        TotalTokenCount = usage.TotalTokenCount,
                    }
                    : null,
                ToolCalls = result.Diagnostics?.ToolCalls
                    .Select(toolCall => new RunAgentToolCall { Name = toolCall.Name })
                    .ToArray() ?? [],
                Attachments = result.Diagnostics?.Attachments
                    .Select(attachment => new RunAgentAttachment
                    {
                        MimeType = attachment.MimeType,
                        FileName = attachment.FileName,
                        Base64Content = attachment.Base64Content,
                    })
                    .ToArray() ?? [],
                Events = result.Events.Select(executionEvent => new RunAgentEvent
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
                }).ToArray(),
            });
    }
}
