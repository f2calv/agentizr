using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace CasCap.Client.Pages;

/// <summary>Shows an app's secret-free provider registrations and the policy applied to published definitions.</summary>
public partial class Providers(AdministrationApiClient apiClient) : CancellableComponentBase
{
    private string? _loadedAppId;
    private ApiResult<ProviderCatalogueResponse>? _result;

    /// <summary>Gets or sets the app selected by the route.</summary>
    [Parameter]
    public string AppId { get; set; } = string.Empty;

    private List<BreadcrumbItem> Breadcrumbs =>
    [
        new("Apps", href: "/"),
        new(AppId, href: $"apps/{Uri.EscapeDataString(AppId)}"),
        new("Providers", href: null, disabled: true),
    ];

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        if (_loadedAppId == AppId)
            return;
        _loadedAppId = AppId;
        await LoadAsync();
    }

    private static string Join(IReadOnlyList<string> values) => values.Count == 0 ? "None" : string.Join(", ", values);

    private Task LoadAsync() => RunAsync(
        async cancellationToken =>
        {
            _result = null;
            _result = await apiClient.GetProvidersAsync(AppId, cancellationToken);
        },
        renew: true);
}
