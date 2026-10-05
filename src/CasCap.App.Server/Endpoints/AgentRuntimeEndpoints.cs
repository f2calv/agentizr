using CasCap.AgentRuntime.Contracts.V1;
using CasCap.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps version 1 Agent Runtime endpoints.</summary>
public static class AgentRuntimeEndpoints
{
    /// <summary>Maps the version 1 agent execution surface.</summary>
    public static WebApplication MapAgentRuntime(this WebApplication app)
    {
        app.MapPost("/api/v1/agents/{agentName}/runs", RunAgentAsync)
            .WithName("RunAgentV1")
            .WithTags("Agent Runtime")
            .WithSummary("Runs one turn against a tenant-scoped agent session")
            .Produces<RunAgentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

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
        }, cancellationToken);

        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new RunAgentResponse
            {
                SessionId = request.SessionId,
                OutputText = result.OutputText,
                DefinitionVersion = result.DefinitionVersion,
            });
    }
}
