using CasCap.Client.Extensions;
using Microsoft.AspNetCore.Components;

namespace CasCap.Client.Components;

/// <summary>Shows whether an administrator is signed in and offers sign-in or sign-out.</summary>
public partial class SessionStatus(AdministrationApiClient apiClient, NavigationManager navigationManager) : ComponentBase
{
    private AdministrationSessionResponse? _session;

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync() =>
        _session = (await apiClient.GetSessionAsync(CancellationToken.None)).Value;

    private void SignIn() => navigationManager.NavigateToSignIn();
}
