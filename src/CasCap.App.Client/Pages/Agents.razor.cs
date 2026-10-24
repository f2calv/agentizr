using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace CasCap.Client.Pages;

/// <summary>Pages an app's agents, including inactive and never-activated ones, with search kept in the URL.</summary>
public partial class Agents(AdministrationApiClient apiClient, NavigationManager navigationManager) : CancellableComponentBase
{
    private const int PageSize = 25;

    private (string AppId, string? Search, int Page)? _loaded;
    private ApiResult<AgentSummaryPageResponse>? _result;
    private string? _searchInput;

    /// <summary>Gets or sets the app selected by the route.</summary>
    [Parameter]
    public string AppId { get; set; } = string.Empty;

    /// <summary>Gets or sets the agent name filter from the query string.</summary>
    [SupplyParameterFromQuery(Name = "search")]
    public string? Search { get; set; }

    /// <summary>Gets or sets the one-based page from the query string.</summary>
    [SupplyParameterFromQuery(Name = "page")]
    public int? PageNumber { get; set; }

    /// <summary>Builds the link to one agent's details.</summary>
    public static string AgentPath(string appId, string agentName) =>
        $"apps/{Uri.EscapeDataString(appId)}/agents/{Uri.EscapeDataString(agentName)}";

    private int Page => Math.Max(PageNumber ?? 1, 1);

    private int PageCount => _result?.Value is { } page ? (int)Math.Ceiling(page.TotalCount / (double)PageSize) : 0;

    private string ProvidersPath => $"apps/{Uri.EscapeDataString(AppId)}/providers";

    private string NewAgentPath => $"apps/{Uri.EscapeDataString(AppId)}/new-agent";

    private List<BreadcrumbItem> Breadcrumbs => [new("Apps", href: "/"), new(AppId, href: null, disabled: true)];

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        var selection = (AppId, Search, Page);
        if (_loaded == selection)
            return;
        _loaded = selection;
        _searchInput = Search;
        await ReloadAsync();
    }

    private Task ReloadAsync() => RunAsync(
        async cancellationToken =>
        {
            _result = null;
            _result = await apiClient.GetAgentsAsync(AppId, Search, Page, PageSize, cancellationToken);
        },
        renew: true);

    private void SearchChanged(string? value)
    {
        _searchInput = value;
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            ["search"] = string.IsNullOrWhiteSpace(value) ? null : value.Trim(),
            ["page"] = null,
        }));
    }

    private void PageChanged(int page) =>
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameter("page", page));
}
