using CasCap.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace CasCap.Services;

/// <summary>Maps session ownership and definition-policy conflicts to explicit HTTP conflict responses.</summary>
internal sealed class AgentSessionConflictHandler : IExceptionHandler
{
    /// <inheritdoc/>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not (AgentSessionConflictException or StatelessAgentSessionException) || httpContext.Response.HasStarted)
            return false;

        await Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: exception is StatelessAgentSessionException ? "Session unavailable" : "Session conflict",
            detail: exception.Message).ExecuteAsync(httpContext);
        return true;
    }
}
