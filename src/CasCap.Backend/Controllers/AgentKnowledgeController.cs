using CasCap.AgentRuntime.Contracts.V1;
using CasCap.AgentRuntime.Contracts.V1.Constants;
using CasCap.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>Exposes private, tenant-qualified document search separately from agent execution permissions.</summary>
[ApiController]
[Tags("Agent Runtime Knowledge")]
[Authorize(Policy = AgentRuntimePolicies.KnowledgeSearch)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AgentKnowledgeController(DocumentKnowledgeService documentKnowledgeSvc) : ControllerBase
{
    /// <inheritdoc cref="DocumentKnowledgeService.SearchAsync"/>
    [HttpPost(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.KnowledgeSearch, Name = "SearchAgentKnowledgeV1")]
    [EndpointSummary("Searches current reviewed passages using the agent's configured embedding profile")]
    [RequestSizeLimit(16_384)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<Results<Ok<SearchKnowledgeResponse>, NotFound, BadRequest<ValidationProblemDetails>, StatusCodeHttpResult>> SearchAsync(
        string agentName, [FromBody] SearchKnowledgeRequest request, CancellationToken cancellationToken)
    {
        var outcome = await documentKnowledgeSvc.SearchAsync(agentName, request, cancellationToken);
        if (outcome.ValidationError is { } error)
            return TypedResults.BadRequest(new ValidationProblemDetails(
                new Dictionary<string, string[]> { [nameof(SearchKnowledgeRequest)] = [error] })
            {
                Status = StatusCodes.Status400BadRequest,
            });
        if (outcome.TimedOut)
            return TypedResults.StatusCode(StatusCodes.Status504GatewayTimeout);
        if (outcome.ProviderFailed)
            return TypedResults.StatusCode(StatusCodes.Status502BadGateway);
        return outcome.Response is { } response ? TypedResults.Ok(response) : TypedResults.NotFound();
    }

    /// <inheritdoc cref="DocumentKnowledgeService.ReadPassageAsync"/>
    [HttpGet(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.KnowledgePassage, Name = "GetAgentKnowledgePassageV1")]
    [EndpointSummary("Reauthorizes and previews one current published source passage without inference")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<Results<Ok<KnowledgePassageResponse>, NotFound>> ReadPassageAsync(
        string agentName, [StringLength(64, MinimumLength = 1)] string indexJobId,
        [Range(0, int.MaxValue)] int ordinal, CancellationToken cancellationToken) =>
        await documentKnowledgeSvc.ReadPassageAsync(agentName, indexJobId, ordinal, cancellationToken) is { } passage
            ? TypedResults.Ok(passage)
            : TypedResults.NotFound();
}
