using CasCap.AgentRuntime.Contracts.V1.Constants;
using CasCap.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace CasCap.Controllers;

/// <summary>Starts and ends tenant-scoped realtime voice calls brokered over WebRTC.</summary>
[ApiController]
[Tags("Agent Runtime Realtime")]
[Authorize(Policy = AgentRuntimePolicies.Realtime)]
public sealed class RealtimeCallsController(RealtimeCallService realtimeCallSvc) : ControllerBase
{
    private const int MaximumSdpOfferCharacters = 64 * 1024;

    /// <summary>Exchanges a WebRTC SDP offer for an answer; the runtime controls the call until it ends.</summary>
    [HttpPost(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.RealtimeCalls, Name = "CreateRealtimeCallV1")]
    [Consumes(RealtimeMediaTypes.Sdp)]
    [EndpointSummary("Starts a brokered realtime voice call from an SDP offer")]
    [ProducesResponseType<string>(StatusCodes.Status201Created, RealtimeMediaTypes.Sdp)]
    public async Task<Results<ContentHttpResult, BadRequest, NotFound, StatusCodeHttpResult>> CreateAsync(
        string agentName, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var buffer = new char[MaximumSdpOfferCharacters + 1];
        var length = await reader.ReadBlockAsync(buffer, cancellationToken);
        if (length is 0 or > MaximumSdpOfferCharacters)
            return TypedResults.BadRequest();

        var outcome = await realtimeCallSvc.CreateAsync(agentName, new string(buffer, 0, length), cancellationToken);
        if (outcome is null)
            return TypedResults.NotFound();
        if (outcome.IsAtCapacity)
            return TypedResults.StatusCode(StatusCodes.Status429TooManyRequests);
        if (outcome.IsProviderUnavailable || outcome.CallId is null)
            return TypedResults.StatusCode(StatusCodes.Status502BadGateway);

        Response.Headers.Location = (AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.RealtimeCall)
            .Replace("{agentName}", Uri.EscapeDataString(agentName), StringComparison.Ordinal)
            .Replace("{callId}", outcome.CallId, StringComparison.Ordinal);
        return TypedResults.Content(outcome.SdpAnswer, RealtimeMediaTypes.Sdp, Encoding.UTF8, StatusCodes.Status201Created);
    }

    /// <summary>Ends a realtime voice call owned by the caller's tenant.</summary>
    [HttpDelete(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.RealtimeCall, Name = "EndRealtimeCallV1")]
    [EndpointSummary("Ends a realtime voice call")]
    public async Task<Results<NoContent, NotFound>> EndAsync(string agentName, string callId) =>
        await realtimeCallSvc.EndAsync(agentName, callId)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
}
