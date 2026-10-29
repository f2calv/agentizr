using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace CasCap.Client.Pages;

/// <summary>Pages app-owned admitted documents without performing source or preparation mutations.</summary>
public partial class Documents(AdministrationApiClient apiClient, NavigationManager navigationManager) : CancellableComponentBase
{
    private const int PageSize = 25;
    private (string AppId, string? Collection, int Page)? _loaded;
    private ApiResult<DocumentCataloguePageResponse>? _result;
    private string? _collectionInput;

    /// <summary>Gets or sets the app selected by the route.</summary>
    [Parameter]
    public string AppId { get; set; } = string.Empty;

    /// <summary>Gets or sets an exact collection filter from the query string.</summary>
    [SupplyParameterFromQuery(Name = "collection")]
    public string? Collection { get; set; }

    /// <summary>Gets or sets the one-based page from the query string.</summary>
    [SupplyParameterFromQuery(Name = "page")]
    public int? PageNumber { get; set; }

    /// <summary>Builds the browser route to an app's document catalogue.</summary>
    public static string Path(string appId) => $"apps/{Uri.EscapeDataString(appId)}/documents";

    private int Page => Math.Clamp(PageNumber ?? 1, 1, 10_000);

    private int PageCount => _result?.Value is { } page ? (int)Math.Ceiling(page.TotalCount / (double)PageSize) : 0;

    private List<BreadcrumbItem> Breadcrumbs =>
        [new("Apps", href: "/"), new(AppId, href: Apps.AppPath(AppId)), new("Documents", href: null, disabled: true)];

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        var selection = (AppId, Collection, Page);
        if (_loaded == selection)
            return;
        _loaded = selection;
        _collectionInput = Collection;
        await ReloadAsync();
    }

    private Task ReloadAsync() => RunAsync(async token =>
    {
        _result = null;
        var result = await apiClient.GetDocumentsAsync(AppId, Collection, Page, PageSize, token);
        token.ThrowIfCancellationRequested();
        _result = result;
    }, renew: true);

    private void CollectionChanged(string? value)
    {
        _collectionInput = value;
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            ["collection"] = string.IsNullOrWhiteSpace(value) ? null : value.Trim(),
            ["page"] = null,
        }));
    }

    private void PageChanged(int page) =>
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameter("page", page));
}
