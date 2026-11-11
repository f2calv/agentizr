using CasCap.AgentRuntime.Contracts.V1;
using CasCap.AgentRuntime.Contracts.V1.Constants;
using CasCap.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>Starts and ends tenant-scoped realtime voice calls brokered over WebRTC.</summary>
[ApiController]
[Tags("Agent Runtime Realtime")]
[Authorize(Policy = AgentRuntimePolicies.Realtime)]
public sealed class RealtimeCallsController(RealtimeCallService realtimeCallSvc) : ControllerBase
{
    /// <summary>Exchanges a WebRTC SDP offer for an answer; the runtime controls the call until it ends.</summary>
    [HttpPost(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.RealtimeCalls, Name = "CreateRealtimeCallV1")]
    [EndpointSummary("Starts a brokered realtime voice call from an SDP offer")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<Results<Created<RealtimeCallResponse>, NotFound, StatusCodeHttpResult>> CreateAsync(
        string agentName, [FromBody] CreateRealtimeCallRequest request, CancellationToken cancellationToken)
    {
        var outcome = await realtimeCallSvc.CreateAsync(agentName, request.SdpOffer, cancellationToken);
        if (outcome is null)
            return TypedResults.NotFound();
        if (outcome.IsAtCapacity)
            return TypedResults.StatusCode(StatusCodes.Status429TooManyRequests);
        if (outcome.Response is not { } response)
            return TypedResults.StatusCode(StatusCodes.Status502BadGateway);

        var location = (AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.RealtimeCall)
            .Replace("{agentName}", Uri.EscapeDataString(agentName), StringComparison.Ordinal)
            .Replace("{callId}", response.CallId, StringComparison.Ordinal);
        return TypedResults.Created(location, response);
    }

    /// <summary>
    /// Ends a realtime voice call owned by the caller's tenant and reports why it ended and what it used; a call the
    /// runtime already ended, for example at its maximum duration, reports its retained summary.
    /// </summary>
    [HttpDelete(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.RealtimeCall, Name = "EndRealtimeCallV1")]
    [EndpointSummary("Ends a realtime voice call and returns its summary")]
    public async Task<Results<Ok<RealtimeCallSummaryResponse>, NotFound>> EndAsync(
        string agentName, string callId, CancellationToken cancellationToken) =>
        await realtimeCallSvc.EndAsync(agentName, callId, cancellationToken) is { } summary
            ? TypedResults.Ok(summary)
            : TypedResults.NotFound();
}
