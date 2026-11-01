using CasCap.AgentRuntime.Contracts.V1;
using CasCap.AgentRuntime.Contracts.V1.Constants;
using CasCap.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>Exposes version 1 tenant-scoped execution and session operations.</summary>
[ApiController]
[Tags("Agent Runtime")]
[Authorize(Policy = AgentRuntimePolicies.Execute)]
public sealed class AgentRuntimeController(AgentRuntimeApiService runtimeApiSvc) : ControllerBase
{
    /// <inheritdoc cref="AgentRuntimeApiService.RunAgentAsync"/>
    [HttpPost(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.Runs, Name = "RunAgentV1")]
    [EndpointSummary("Runs one turn against a tenant-scoped agent session")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<Results<Ok<RunAgentResponse>, NotFound>> RunAgentAsync(
        string agentName, [FromBody] RunAgentRequest request, CancellationToken cancellationToken) =>
        await runtimeApiSvc.RunAgentAsync(agentName, request, cancellationToken) is { } response
            ? TypedResults.Ok(response)
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentRuntimeApiService.StreamAgentAsync"/>
    [HttpPost(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.RunStream, Name = "StreamAgentV1")]
    [EndpointSummary("Streams execution events followed by the final tenant-scoped agent response")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<Ok<IAsyncEnumerable<RunAgentStreamItem>>> StreamAgentAsync(
        string agentName, [FromBody] RunAgentRequest request, CancellationToken cancellationToken) =>
        TypedResults.Ok(await runtimeApiSvc.StreamAgentAsync(agentName, request, cancellationToken));

    /// <inheritdoc cref="AgentRuntimeApiService.GetSessionAsync"/>
    [HttpGet(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.Session, Name = "GetAgentSessionV1")]
    [EndpointSummary("Gets tenant-scoped agent session status")]
    public async Task<Results<Ok<AgentSessionInfoResponse>, NotFound>> GetSessionAsync(
        string agentName, string sessionId, CancellationToken cancellationToken) =>
        await runtimeApiSvc.GetSessionAsync(agentName, sessionId, cancellationToken) is { } response
            ? TypedResults.Ok(response)
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentRuntimeApiService.ResetSessionAsync"/>
    [HttpDelete(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.Session, Name = "ResetAgentSessionV1")]
    [EndpointSummary("Resets tenant-scoped active agent session state")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<Results<NoContent, NotFound>> ResetSessionAsync(
        string agentName, string sessionId, CancellationToken cancellationToken) =>
        await runtimeApiSvc.ResetSessionAsync(agentName, sessionId, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentRuntimeApiService.CompactSessionAsync"/>
    [HttpPost(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.SessionCompaction, Name = "CompactAgentSessionV1")]
    [EndpointSummary("Compacts tenant-scoped active agent session history")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<Results<Ok<CompactAgentSessionResponse>, NotFound>> CompactSessionAsync(
        string agentName, string sessionId, [FromBody] CompactAgentSessionRequest request, CancellationToken cancellationToken) =>
        await runtimeApiSvc.CompactSessionAsync(agentName, sessionId, request, cancellationToken) is { } response
            ? TypedResults.Ok(response)
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentRuntimeApiService.SaveSessionSnapshotAsync"/>
    [HttpPut(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.SessionSnapshot, Name = "SaveAgentSessionSnapshotV1")]
    [EndpointSummary("Saves active agent session state as a named snapshot")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<Results<NoContent, NotFound>> SaveSessionSnapshotAsync(
        string agentName, string sessionId, string snapshotName, CancellationToken cancellationToken) =>
        await runtimeApiSvc.SaveSessionSnapshotAsync(agentName, sessionId, snapshotName, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentRuntimeApiService.LoadSessionSnapshotAsync"/>
    [HttpPost(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.SessionSnapshotActivation, Name = "LoadAgentSessionSnapshotV1")]
    [EndpointSummary("Loads a named snapshot into the active agent session")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<Results<NoContent, NotFound>> LoadSessionSnapshotAsync(
        string agentName, string sessionId, string snapshotName, CancellationToken cancellationToken) =>
        await runtimeApiSvc.LoadSessionSnapshotAsync(agentName, sessionId, snapshotName, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentRuntimeApiService.DeleteSessionSnapshotAsync"/>
    [HttpDelete(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.SessionSnapshot, Name = "DeleteAgentSessionSnapshotV1")]
    [EndpointSummary("Deletes a named agent session snapshot")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<Results<NoContent, NotFound>> DeleteSessionSnapshotAsync(
        string agentName, string sessionId, string snapshotName, CancellationToken cancellationToken) =>
        await runtimeApiSvc.DeleteSessionSnapshotAsync(agentName, sessionId, snapshotName, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentRuntimeApiService.GetOverridesAsync"/>
    [HttpGet(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.SessionOverrides, Name = "GetAgentOverridesV1")]
    [EndpointSummary("Gets complete per-session agent runtime overrides")]
    public async Task<Results<Ok<AgentOverridesResponse>, NotFound>> GetOverridesAsync(
        string agentName, string sessionId, CancellationToken cancellationToken) =>
        await runtimeApiSvc.GetOverridesAsync(agentName, sessionId, cancellationToken) is { } response
            ? TypedResults.Ok(response)
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentRuntimeApiService.SetOverridesAsync"/>
    [HttpPut(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.SessionOverrides, Name = "SetAgentOverridesV1")]
    [EndpointSummary("Replaces complete per-session agent runtime overrides")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<Results<Ok<AgentOverridesResponse>, NotFound>> SetOverridesAsync(
        string agentName, string sessionId, [FromBody] UpdateAgentOverridesRequest request, CancellationToken cancellationToken) =>
        await runtimeApiSvc.SetOverridesAsync(agentName, sessionId, request, cancellationToken) is { } response
            ? TypedResults.Ok(response)
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentRuntimeApiService.GetInstructionsAsync"/>
    [HttpGet(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.SessionInstructions, Name = "GetAgentInstructionsV1")]
    [EndpointSummary("Gets the effective instructions when the tenant enables disclosure")]
    public async Task<Results<Ok<AgentInstructionsResponse>, NotFound>> GetInstructionsAsync(
        string agentName, string sessionId, CancellationToken cancellationToken) =>
        await runtimeApiSvc.GetInstructionsAsync(agentName, sessionId, cancellationToken) is { } response
            ? TypedResults.Ok(response)
            : TypedResults.NotFound();
}
