using CasCap.AgentRuntime.Contracts.V1;
using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CasCap.Services;

/// <summary>
/// Controls one brokered realtime call through its server-side WebSocket: reapplies the definition's session,
/// executes allowlisted tool calls, meters token usage and hangs up on request, at the maximum duration or when
/// control is lost.
/// </summary>
internal sealed class RealtimeCallSession : IAsyncDisposable
{
    private const int NoReason = -1;
    private static readonly TimeSpan HangUpTimeout = TimeSpan.FromSeconds(5);

    private readonly RealtimeCallSessionOptions _options;
    private readonly AzureRealtimeClient _azure;
    private readonly RealtimeToolDispatcher _dispatcher;
    private readonly IReadOnlyList<IAsyncDisposable> _toolLeases;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly RealtimeCallUsageMeter _meter = new();
    private CancellationTokenSource? _deadline;
    private Task _run = Task.CompletedTask;
    private long _startedAt;
    private int _endReason = NoReason;

    /// <summary>Initializes a call that has been answered by the provider but is not yet controlled.</summary>
    public RealtimeCallSession(
        RealtimeCallSessionOptions options,
        AzureRealtimeClient azure,
        RealtimeToolDispatcher dispatcher,
        IReadOnlyList<IAsyncDisposable> toolLeases,
        ILogger logger)
    {
        _options = options;
        _azure = azure;
        _dispatcher = dispatcher;
        _toolLeases = toolLeases;
        _logger = logger;
    }

    /// <summary>Gets the opaque runtime call identifier.</summary>
    public string CallId => _options.CallId;

    /// <summary>Gets the owning tenant.</summary>
    public string TenantId => _options.TenantId;

    /// <summary>Gets the agent the call belongs to.</summary>
    public string AgentName => _options.AgentName;

    /// <summary>Raised once after the call has ended and its resources are released.</summary>
    public event EventHandler? Ended;

    /// <summary>Gets how the call ended and what it used, or <see langword="null"/> while it is active.</summary>
    public RealtimeCallSummaryResponse? Summary { get; private set; }

    /// <summary>Starts controlling the call in the background until it ends.</summary>
    public void Start()
    {
        _startedAt = Stopwatch.GetTimestamp();
        _deadline = new CancellationTokenSource(_options.MaximumDuration);
        _deadline.Token.Register(() => RequestEnd(RealtimeCallEndReason.MaximumDuration));
        _run = Task.Run(() => RunAsync(_lifetime.Token), CancellationToken.None);
    }

    /// <summary>Ends the call for the given reason, unless it already ended, and returns its summary.</summary>
    public async Task<RealtimeCallSummaryResponse> EndAsync(RealtimeCallEndReason reason)
    {
        RequestEnd(reason);
        await _run;
        return Summary!;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await EndAsync(RealtimeCallEndReason.HostShutdown);
        _deadline?.Dispose();
        _lifetime.Dispose();
        _sendLock.Dispose();
    }

    // The first reason wins, so a later hang-up request cannot relabel a call that already ended.
    private void RequestEnd(RealtimeCallEndReason reason)
    {
        Interlocked.CompareExchange(ref _endReason, (int)reason, NoReason);
        _lifetime.Cancel();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var socket = new ClientWebSocket();
            var (name, value) = await _options.Credential.GetHeaderAsync(cancellationToken);
            socket.Options.SetRequestHeader(name, value);
            await socket.ConnectAsync(AzureRealtimeClient.GetControlUri(_options.Endpoint, _options.ProviderCallId), cancellationToken);
            await SendAsync(socket, new JsonObject
            {
                ["type"] = "session.update",
                ["session"] = _options.Session.DeepClone(),
            }, cancellationToken);
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("{ClassName} controls a realtime call", nameof(RealtimeCallSession));
            await ReceiveAsync(socket, cancellationToken);
            Interlocked.CompareExchange(ref _endReason, (int)RealtimeCallEndReason.ProviderClosed, NoReason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ended on request, at the maximum duration or on shutdown; the reason is already recorded.
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or InvalidOperationException or JsonException)
        {
            Interlocked.CompareExchange(ref _endReason, (int)RealtimeCallEndReason.ControlLost, NoReason);
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(ex, "{ClassName} lost control of a realtime call", nameof(RealtimeCallSession));
        }
        finally
        {
            await HangUpAsync();
            foreach (var lease in _toolLeases)
                await lease.DisposeAsync();
            Summary = CreateSummary();
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation(
                    "{ClassName} realtime call ended by {EndReason} after {DurationSeconds}s using {TotalTokens} tokens",
                    nameof(RealtimeCallSession), Summary.EndReason, Summary.DurationSeconds, Summary.Usage.TotalTokens);
            Ended?.Invoke(this, EventArgs.Empty);
        }
    }

    private RealtimeCallSummaryResponse CreateSummary()
    {
        var reason = Volatile.Read(ref _endReason);
        return new RealtimeCallSummaryResponse
        {
            EndReason = reason == NoReason ? RealtimeCallEndReason.ControlLost : (RealtimeCallEndReason)reason,
            DurationSeconds = (int)Stopwatch.GetElapsedTime(_startedAt).TotalSeconds,
            Usage = _meter.ToUsage(),
            EstimatedCost = _meter.EstimateCost(_options.Pricing),
        };
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(64 * 1024);
        while (true)
        {
            buffer.ResetWrittenCount();
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.GetMemory(16 * 1024), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                buffer.Advance(result.Count);
            }
            while (!result.EndOfMessage);

            using var document = JsonDocument.Parse(buffer.WrittenMemory);
            var root = document.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "response.done":
                    var response = root.GetProperty("response");
                    _meter.Add(response);
                    await ExecuteFunctionCallsAsync(socket, response, cancellationToken);
                    break;
                case "error":
                    var code = root.GetProperty("error").TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
                    if (_logger.IsEnabled(LogLevel.Warning))
                        _logger.LogWarning("{ClassName} realtime provider reported error {ErrorCode}",
                            nameof(RealtimeCallSession), code);
                    break;
            }
        }
    }

    private async Task ExecuteFunctionCallsAsync(ClientWebSocket socket, JsonElement response, CancellationToken cancellationToken)
    {
        if (!response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return;

        var outputs = 0;
        foreach (var item in output.EnumerateArray())
        {
            if (item.GetProperty("type").GetString() != "function_call")
                continue;
            var result = await _dispatcher.InvokeAsync(
                item.GetProperty("call_id").GetString()!,
                item.GetProperty("name").GetString()!,
                item.TryGetProperty("arguments", out var arguments) ? arguments.GetString() ?? string.Empty : string.Empty,
                cancellationToken);
            if (result is null)
                continue;
            await SendAsync(socket, new JsonObject
            {
                ["type"] = "conversation.item.create",
                ["item"] = new JsonObject
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = item.GetProperty("call_id").GetString(),
                    ["output"] = result,
                },
            }, cancellationToken);
            outputs++;
        }
        if (outputs > 0)
            await SendAsync(socket, new JsonObject { ["type"] = "response.create" }, cancellationToken);
    }

    private async Task SendAsync(ClientWebSocket socket, JsonObject message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task HangUpAsync()
    {
        using var timeout = new CancellationTokenSource(HangUpTimeout);
        try
        {
            var accepted = await _azure.HangUpAsync(_options.Endpoint, _options.Credential, _options.ProviderCallId, timeout.Token);
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("{ClassName} ended a realtime call; provider hang-up accepted {Accepted}",
                    nameof(RealtimeCallSession), accepted);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(ex, "{ClassName} could not ask the provider to end a realtime call",
                    nameof(RealtimeCallSession));
        }
    }
}
