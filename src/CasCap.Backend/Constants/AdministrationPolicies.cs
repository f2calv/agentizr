namespace CasCap.Constants;

/// <summary>Authorization policy names for human administration through the browser frontend.</summary>
public static class AdministrationPolicies
{
    /// <summary>Allows managing the app selected by the route, or listing assigned apps when none is selected.</summary>
    public const string AppAdministrator = nameof(AppAdministrator);

    /// <summary>Requires enabled cookie sign-in even when other administration permits open Development access.</summary>
    public const string AuthenticatedDocumentReviewer = nameof(AuthenticatedDocumentReviewer);
}
