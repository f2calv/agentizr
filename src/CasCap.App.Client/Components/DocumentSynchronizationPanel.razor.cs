using Microsoft.AspNetCore.Components;
using MudBlazor;
using System.Globalization;

namespace CasCap.Client.Components;

/// <summary>Requests confirmed application-wide reconciliation and displays its safe durable progress.</summary>
public partial class DocumentSynchronizationPanel(
    AdministrationApiClient apiClient, IDialogService dialogService) : CancellableComponentBase
{
    private (string App, int Refresh)? _loaded;
    private ApiResult<DocumentSynchronizationResponse>? _result;
    private ApiResult<DocumentSynchronizationResponse>? _request;
    private bool _busy;

    /// <summary>Gets or sets the application selected by the parent catalogue.</summary>
    [Parameter]
    public string AppId { get; set; } = string.Empty;

    /// <summary>Gets or sets the parent catalogue's explicit refresh generation.</summary>
    [Parameter]
    public int RefreshVersion { get; set; }

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        var selection = (AppId, RefreshVersion);
        if (_loaded == selection)
            return;
        _loaded = selection;
        await ReloadAsync();
    }

    private Task ReloadAsync() => RunAsync(async token =>
    {
        _result = null;
        _request = null;
        var result = await apiClient.GetDocumentSynchronizationAsync(AppId, token);
        token.ThrowIfCancellationRequested();
        _result = result;
    }, renew: true);

    private async Task SynchronizeAsync()
    {
        if (_busy || _result?.Value is not { Enabled: true, Sources.Count: > 0 })
            return;
        var app = AppId;
        var selection = _loaded;
        _busy = true;
        try
        {
            var confirmed = await dialogService.ShowMessageBoxAsync(
                "Sync documents in all collections?",
                "This reconciles all configured collections for this app, regardless of the catalogue filter. "
                    + "Missing PDFs have their stored chunks and review content removed. Original blobs are never changed. "
                    + "This does not enable processing or search.",
                yesText: "Sync documents", cancelText: "Cancel");
            if (confirmed != true || selection != _loaded)
                return;
            await RunAsync(async token =>
            {
                var result = await apiClient.RequestDocumentSynchronizationAsync(app, token);
                token.ThrowIfCancellationRequested();
                _request = result;
                if (result.IsSuccess)
                    _result = result;
            });
        }
        finally
        {
            _busy = false;
        }
    }

    private static string TimeLabel(DateTimeOffset? timestamp) =>
        timestamp?.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "Not yet";

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _loaded = null;
        base.Dispose(disposing);
    }
}
