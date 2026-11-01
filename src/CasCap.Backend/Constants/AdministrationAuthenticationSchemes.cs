namespace CasCap.Constants;

/// <summary>Authentication scheme names for human administration, separate from machine bearer tokens.</summary>
public static class AdministrationAuthenticationSchemes
{
    /// <summary>The HttpOnly session cookie issued after administrator sign-in.</summary>
    public const string Cookie = "AdministrationCookie";

    /// <summary>The OpenID Connect authorization-code flow against the administration web registration.</summary>
    public const string OpenIdConnect = "AdministrationOidc";
}
