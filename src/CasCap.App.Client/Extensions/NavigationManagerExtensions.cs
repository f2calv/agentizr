using Microsoft.AspNetCore.Components;

namespace CasCap.Client.Extensions;

/// <summary>Navigation helpers for host-owned administration routes that the client router must not handle.</summary>
public static class NavigationManagerExtensions
{
    /// <summary>Starts host sign-in with a full page load, returning to the current page afterwards.</summary>
    public static void NavigateToSignIn(this NavigationManager navigationManager)
    {
        var returnUrl = "/" + navigationManager.ToBaseRelativePath(navigationManager.Uri);
        navigationManager.NavigateTo(
            $"{AdministrationRoutes.Login}?returnUrl={Uri.EscapeDataString(returnUrl)}",
            forceLoad: true);
    }
}
