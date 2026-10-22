using CasCap.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace CasCap.Services;

/// <summary>Maps session ownership conflicts to an explicit HTTP conflict response.</summary>
internal sealed class AgentSessionConflictHandler : IExceptionHandler
{
    /// <inheritdoc/>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not AgentSessionConflictException || httpContext.Response.HasStarted)
            return false;

        await Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Session conflict",
            detail: exception.Message).ExecuteAsync(httpContext);
        return true;
    }
}
