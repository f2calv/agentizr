using CasCap.Client.Models;
using CasCap.Constants;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace CasCap.Services;

/// <summary>Describes, starts and ends browser administrator sessions issued by the host's own cookie.</summary>
public sealed class AdministrationSessionService
{
    private readonly IOptions<AdministrationConfig> _administrationConfig;
    private readonly IAntiforgery _antiforgery;
    private readonly AdministrationSetupService _setupSvc;

    /// <summary>Creates the session service.</summary>
    internal AdministrationSessionService(
        IOptions<AdministrationConfig> administrationConfig,
        IAntiforgery antiforgery,
        AdministrationSetupService setupSvc)
    {
        _administrationConfig = administrationConfig;
        _antiforgery = antiforgery;
        _setupSvc = setupSvc;
    }

    /// <summary>Gets whether the host requires administrator sign-in.</summary>
    public bool SignInRequired => _administrationConfig.Value.Enabled;

    /// <summary>
    /// Gets an HTML explanation when sign-in cannot succeed from this request, or <see langword="null" /> when it can start.
    /// </summary>
    public string? GetSignInSetupProblemPage(HttpRequest request) =>
        AdministrationSetupService.IsForwardedSchemeIgnored(request) ? _setupSvc.RenderSignInUnavailablePage(request) : null;

    /// <summary>Describes the session and issues an antiforgery token bound to the signed-in administrator.</summary>
    public async Task<AdministrationSessionResponse> GetSessionAsync(HttpContext httpContext)
    {
        await UseAdministratorPrincipalAsync(httpContext);
        var tokens = _antiforgery.GetAndStoreTokens(httpContext);
        var isAuthenticated = SignInRequired && httpContext.User.Identity?.IsAuthenticated is true;
        return new AdministrationSessionResponse
        {
            SignInRequired = SignInRequired,
            IsAuthenticated = isAuthenticated,
            DisplayName = isAuthenticated ? httpContext.User.Identity?.Name : null,
            AntiforgeryToken = tokens.RequestToken!,
        };
    }

    /// <summary>Returns whether a sign-out request carries a valid antiforgery token for the signed-in administrator.</summary>
    public async Task<bool> IsSignOutRequestValidAsync(HttpContext httpContext)
    {
        await UseAdministratorPrincipalAsync(httpContext);
        return await _antiforgery.IsRequestValidAsync(httpContext);
    }

    /// <summary>Returns the requested path when it stays on this host, otherwise the root path.</summary>
    public static string GetLocalReturnUrl(string? returnUrl) =>
        returnUrl is ['/', not ('/' or '\\'), ..] or "/" ? returnUrl : "/";

    // The default scheme authenticates machine callers; administration endpoints use the cookie principal.
    private async Task UseAdministratorPrincipalAsync(HttpContext httpContext)
    {
        if (!SignInRequired)
            return;
        var result = await httpContext.AuthenticateAsync(AdministrationAuthenticationSchemes.Cookie);
        if (result.Succeeded)
            httpContext.User = result.Principal;
    }
}
