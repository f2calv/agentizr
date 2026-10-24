using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace CasCap.Services;

/// <summary>
/// Answers failed administration API authorization with 401 or 403 Problem Details instead of an identity
/// provider redirect, so the browser client can render the state.
/// </summary>
internal sealed class AdministrationAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    /// <inheritdoc/>
    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded
            || context.GetEndpoint() is not { } endpoint
            || !AdministrationAccessService.IsAdministrationEndpoint(endpoint.Metadata))
            return _defaultHandler.HandleAsync(next, context, policy, authorizeResult);

        return Results.Problem(statusCode: authorizeResult.Challenged
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status403Forbidden).ExecuteAsync(context);
    }
}
