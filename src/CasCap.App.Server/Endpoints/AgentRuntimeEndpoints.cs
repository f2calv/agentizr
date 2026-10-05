using CasCap.AgentRuntime.Contracts.V1;
using CasCap.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps version 1 Agent Runtime endpoints.</summary>
public static class AgentRuntimeEndpoints
{
    /// <summary>Maps the version 1 agent execution surface.</summary>
    public static WebApplication MapAgentRuntime(this WebApplication app, bool authorizationRequired)
    {
        var endpoint = app.MapPost("/api/v1/agents/{agentName}/runs", RunAgentAsync)
            .WithName("RunAgentV1")
            .WithTags("Agent Runtime")
            .WithSummary("Runs one turn against a tenant-scoped agent session")
            .Produces<RunAgentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        if (authorizationRequired)
            endpoint.RequireAuthorization();

        return app;
    }

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
