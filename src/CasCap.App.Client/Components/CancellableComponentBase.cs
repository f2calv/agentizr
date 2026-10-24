using Microsoft.AspNetCore.Components;

namespace CasCap.Client.Components;

/// <summary>Owns the cancellation of a component's HTTP calls and treats cancellation by navigation as normal.</summary>
public abstract class CancellableComponentBase : ComponentBase, IDisposable
{
    private CancellationTokenSource _cancellation = new();

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Runs an operation with the current token, ignoring cancellation caused by navigation or disposal.</summary>
    /// <param name="operation">The operation, which receives the component's cancellation token.</param>
    /// <param name="renew">Cancels calls still running for previously selected data before starting.</param>
    protected async Task RunAsync(Func<CancellationToken, Task> operation, bool renew = false)
    {
        if (renew)
        {
            await _cancellation.CancelAsync();
            _cancellation.Dispose();
            _cancellation = new CancellationTokenSource();
        }
        var cancellationToken = _cancellation.Token;
        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The component navigated away or selected different data; nothing is left to render.
        }
    }

    /// <summary>Cancels outstanding calls when the component leaves the page.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
