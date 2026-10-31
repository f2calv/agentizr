using System.Net;
using System.Text;

namespace CasCap.Services;

/// <summary>
/// Explains, as a self-contained HTML page, which configuration keeps the administration frontend or its sign-in from
/// working on this host.
/// </summary>
/// <remarks>The page names settings and whether they are set, never their values.</remarks>
public sealed class AdministrationSetupService
{
    private const string ForwardedProtoHeader = "X-Forwarded-Proto";
    private const string Section = "CasCap:AdministrationConfig";

    private readonly IOptions<AdministrationConfig> _administrationConfig;
    private readonly IOptions<TenantAuthenticationConfig> _tenantAuthenticationConfig;
    private readonly IHostEnvironment _hostEnvironment;

    /// <summary>Creates the setup service.</summary>
    internal AdministrationSetupService(
        IOptions<AdministrationConfig> administrationConfig,
        IOptions<TenantAuthenticationConfig> tenantAuthenticationConfig,
        IHostEnvironment hostEnvironment)
    {
        _administrationConfig = administrationConfig;
        _tenantAuthenticationConfig = tenantAuthenticationConfig;
        _hostEnvironment = hostEnvironment;
    }

    /// <summary>Gets whether a TLS-terminating proxy forwarded HTTPS but the host still sees plain HTTP.</summary>
    /// <remarks>Sign-in would then send the identity provider an <c>http://</c> callback, which it rejects.</remarks>
    public static bool IsForwardedSchemeIgnored(HttpRequest request) =>
        !request.IsHttps
        && string.Equals(request.Headers[ForwardedProtoHeader].ToString(), "https", StringComparison.OrdinalIgnoreCase);

    /// <summary>Renders the page shown instead of the frontend when this host does not serve it.</summary>
    public string RenderFrontendUnavailablePage(HttpRequest request)
    {
        var reason = _hostEnvironment.IsDevelopment() && _tenantAuthenticationConfig.Value.Enabled
            ? "Tenant authentication is enabled but administration sign-in is not. In Development the frontend "
                + "opens without sign-in only when TenantAuthenticationConfig is also disabled; otherwise the "
                + "server would refuse every administration request."
            : "Administration sign-in is not enabled, so this host serves only the agent runtime API.";
        var page = StartPage("The administration frontend is not enabled", reason);
        AppendSettings(page, request);
        AppendForwardedSchemeNote(page, request);
        return EndPage(page);
    }

    /// <summary>Renders the page shown instead of starting sign-in when forwarded HTTPS is being ignored.</summary>
    public string RenderSignInUnavailablePage(HttpRequest request)
    {
        var page = StartPage(
            "Sign-in cannot start on this host",
            "The request reached this host through a proxy that ended HTTPS, but the host is not applying the "
                + "forwarded headers. The identity provider would reject the http:// callback address.");
        AppendForwardedSchemeNote(page, request);
        return EndPage(page);
    }

    private void AppendSettings(StringBuilder page, HttpRequest request)
    {
        var config = _administrationConfig.Value;
        page.Append("<h2>Settings to configure</h2><p>Set these, then restart the host. Keep the client secret in a secret store.</p>")
            .Append("<table><thead><tr><th scope=\"col\">Setting</th><th scope=\"col\">Status</th></tr></thead><tbody>");
        AppendRow(page, nameof(AdministrationConfig.Enabled), config.Enabled ? "true" : "false: set it to true");
        AppendRow(page, nameof(AdministrationConfig.Authority), IsSet(config.Authority));
        AppendRow(page, nameof(AdministrationConfig.ClientId), IsSet(config.ClientId));
        AppendRow(page, nameof(AdministrationConfig.ClientSecret), IsSet(config.ClientSecret));
        AppendRow(
            page,
            nameof(AdministrationConfig.AppAdministrators),
            config.AppAdministrators.Count == 0 ? "missing" : $"set for {config.AppAdministrators.Count} app(s)");
        page.Append("</tbody></table>");

        var origin = $"https://{request.Host}";
        page.Append("<h2>Redirect URIs to register</h2><ul>")
            .Append("<li><code>").Append(Encode(origin + config.CallbackPath)).Append("</code></li>")
            .Append("<li><code>").Append(Encode(origin + config.SignedOutCallbackPath)).Append("</code></li></ul>")
            .Append("<p>The host also answers <code>/status</code> and <code>/healthz</code>. ")
            .Append("See <code>docs/administration-frontend.md</code> in the agentizr repository.</p>");
    }

    private static void AppendForwardedSchemeNote(StringBuilder page, HttpRequest request)
    {
        if (!IsForwardedSchemeIgnored(request))
            return;
        page.Append("<h2>Forwarded headers</h2><p>A proxy forwarded HTTPS, but the host sees HTTP. Set ")
            .Append("<code>ASPNETCORE_FORWARDEDHEADERS_ENABLED=true</code> so sign-in callbacks keep the ")
            .Append("<code>https</code> scheme.</p>");
    }

    private static void AppendRow(StringBuilder page, string setting, string status) =>
        page.Append("<tr><td><code>").Append(Encode($"{Section}:{setting}")).Append("</code></td><td>")
            .Append(Encode(status)).Append("</td></tr>");

    private static string IsSet(string? value) => string.IsNullOrWhiteSpace(value) ? "missing" : "set";

    private static StringBuilder StartPage(string heading, string summary) =>
        new StringBuilder()
            .Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" />")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />")
            .Append("<meta name=\"robots\" content=\"noindex\" /><title>").Append(Encode(heading)).Append(" · agentizr</title>")
            .Append("<style>body{font-family:system-ui,sans-serif;max-width:46rem;margin:3rem auto;padding:0 1rem;")
            .Append("line-height:1.5;color:#1f2328}table{border-collapse:collapse;width:100%}")
            .Append("th,td{text-align:left;padding:.4rem .6rem;border-bottom:1px solid #d0d7de}")
            .Append("code{background:#f6f8fa;padding:.1rem .3rem;border-radius:4px;word-break:break-all}</style></head>")
            .Append("<body><main><h1>").Append(Encode(heading)).Append("</h1><p>").Append(Encode(summary)).Append("</p>");

    private static string EndPage(StringBuilder page) => page.Append("</main></body></html>").ToString();

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
