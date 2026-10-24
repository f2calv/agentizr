namespace CasCap.Client.Pages;

/// <summary>Lists the apps the signed-in administrator may manage.</summary>
public partial class Apps(AdministrationApiClient apiClient) : CancellableComponentBase
{
    private ApiResult<IReadOnlyList<AppSummaryResponse>>? _result;

    /// <summary>Builds the link to an app's agents.</summary>
    public static string AppPath(string appId) => $"apps/{Uri.EscapeDataString(appId)}";

    /// <inheritdoc/>
    protected override Task OnInitializedAsync() => LoadAsync();

    private Task LoadAsync() => RunAsync(async cancellationToken =>
    {
        _result = null;
        _result = await apiClient.GetAppsAsync(cancellationToken);
    });
}
