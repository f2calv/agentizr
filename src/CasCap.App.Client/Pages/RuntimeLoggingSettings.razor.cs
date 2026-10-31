using CasCap.Common.Configuration.Management.Contracts.Models;

namespace CasCap.Client.Pages;

/// <summary>Embeds the shared typed logging editor behind the host's immutable authorized destination.</summary>
public partial class RuntimeLoggingSettings(AdministrationApiClient apiClient) : CancellableComponentBase
{
    private ApiResult<RuntimeConfigurationDestination>? _result;

    /// <inheritdoc/>
    protected override Task OnInitializedAsync() => LoadAsync();

    private Task LoadAsync() => RunAsync(async cancellationToken =>
    {
        _result = null;
        _result = await apiClient.GetRuntimeLoggingDestinationAsync(cancellationToken);
    });
}
