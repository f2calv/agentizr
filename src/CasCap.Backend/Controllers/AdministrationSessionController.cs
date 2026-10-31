using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>Starts, describes and ends browser administrator sessions.</summary>
/// <remarks>Sign-in and sign-out are browser navigations; only the session description is a JSON API.</remarks>
[ApiController]
[Tags("Administration")]
public sealed class AdministrationSessionController(AdministrationSessionService sessionSvc) : ControllerBase
{
    /// <inheritdoc cref="AdministrationSessionService.GetSessionAsync"/>
    [HttpGet(AdministrationRoutes.Session, Name = "GetAdministrationSession")]
    [EndpointSummary("Describes the administrator session and issues an antiforgery token")]
    public async Task<Ok<AdministrationSessionResponse>> GetSessionAsync() =>
        TypedResults.Ok(await sessionSvc.GetSessionAsync(HttpContext));

    /// <summary>Starts OpenID Connect sign-in, returning to a local path afterwards.</summary>
    /// <remarks>Explains the problem instead when the host cannot build an HTTPS callback for this request.</remarks>
    [HttpGet(AdministrationRoutes.Login, Name = "AdministrationLogin")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public Results<ChallengeHttpResult, RedirectHttpResult, ContentHttpResult> Login([FromQuery] string? returnUrl = null)
    {
        if (!sessionSvc.SignInRequired)
            return TypedResults.LocalRedirect(AdministrationSessionService.GetLocalReturnUrl(returnUrl));
        return sessionSvc.GetSignInSetupProblemPage(Request) is { } problemPage
            ? TypedResults.Content(problemPage, "text/html; charset=utf-8", statusCode: StatusCodes.Status503ServiceUnavailable)
            : TypedResults.Challenge(
                new AuthenticationProperties { RedirectUri = AdministrationSessionService.GetLocalReturnUrl(returnUrl) },
                [AdministrationAuthenticationSchemes.OpenIdConnect]);
    }

    /// <summary>Ends the administrator session after validating the posted antiforgery token.</summary>
    [HttpPost(AdministrationRoutes.Logout, Name = "AdministrationLogout")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<Results<SignOutHttpResult, RedirectHttpResult, BadRequest>> LogoutAsync()
    {
        if (!sessionSvc.SignInRequired)
            return TypedResults.LocalRedirect("/");
        if (!await sessionSvc.IsSignOutRequestValidAsync(HttpContext))
            return TypedResults.BadRequest();
        return TypedResults.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [AdministrationAuthenticationSchemes.Cookie, AdministrationAuthenticationSchemes.OpenIdConnect]);
    }
}
