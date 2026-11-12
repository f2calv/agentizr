using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CasCap.Filters;

/// <summary>Requires a valid antiforgery token on every unsafe-method request to the decorated administration action.</summary>
/// <remarks>
/// Controllers registered with <c>AddControllers</c> lack MVC's view-feature antiforgery filters, so this runs the same
/// validation against the administrator principal the token is bound to. Actions authorized by a cookie policy already
/// carry that principal; anonymous-reachable actions such as sign-out select the administration cookie here.
/// </remarks>
internal sealed class ValidateAdministrationAntiforgeryAttribute() : TypeFilterAttribute(typeof(Filter))
{
    private sealed class Filter(IAntiforgery antiforgery, AdministrationSessionService sessionSvc) : IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var method = context.HttpContext.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
                return;
            if (context.HttpContext.User.Identity?.IsAuthenticated is not true)
                await sessionSvc.UseAdministratorPrincipalAsync(context.HttpContext);
            if (!await antiforgery.IsRequestValidAsync(context.HttpContext))
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "The antiforgery token is missing or invalid.",
                })
                { StatusCode = StatusCodes.Status400BadRequest };
        }
    }
}
