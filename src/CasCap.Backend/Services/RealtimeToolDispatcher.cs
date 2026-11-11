using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CasCap.Services;

/// <summary>Executes a realtime call's function calls against its allowlisted tools, once per call identifier.</summary>
/// <remarks>Arguments and results are model and learner content; they are never logged.</remarks>
internal sealed class RealtimeToolDispatcher(IReadOnlyCollection<AIFunction> tools, TimeSpan toolTimeout, ILogger logger)
{
    private readonly Dictionary<string, AIFunction> _tools = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
    private readonly HashSet<string> _handledCallIds = new(StringComparer.Ordinal);

    /// <summary>Invokes one function call, or returns <see langword="null"/> when this call id was already handled.</summary>
    /// <returns>The JSON output to return to the model.</returns>
    public async Task<string?> InvokeAsync(
        string callId,
        string name,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        if (!_handledCallIds.Add(callId))
            return null;

        if (!_tools.TryGetValue(name, out var tool))
        {
            logger.LogWarning("{ClassName} refused realtime call to tool {ToolName} outside the allowlist",
                nameof(RealtimeToolDispatcher), name);
            return Error($"Tool '{name}' is not available in this call.");
        }

        AIFunctionArguments arguments;
        try
        {
            arguments = ParseArguments(argumentsJson);
        }
        catch (JsonException)
        {
            return Error("The tool arguments were not a JSON object.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(toolTimeout);
        try
        {
            var result = await tool.InvokeAsync(arguments, timeout.Token);
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("{ClassName} completed realtime tool {ToolName}", nameof(RealtimeToolDispatcher), name);
            return result switch
            {
                null => "null",
                string text => JsonSerializer.Serialize(text),
                JsonElement element => element.GetRawText(),
                _ => JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("{ClassName} realtime tool {ToolName} timed out", nameof(RealtimeToolDispatcher), name);
            return Error($"Tool '{name}' timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A tool error message can echo learner content, so only the exception type is logged.
            logger.LogWarning("{ClassName} realtime tool {ToolName} failed with {ExceptionType}",
                nameof(RealtimeToolDispatcher), name, ex.GetType().Name);
            return Error($"Tool '{name}' failed.");
        }
    }

    private static AIFunctionArguments ParseArguments(string argumentsJson)
    {
        var arguments = new AIFunctionArguments();
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return arguments;
        using var document = JsonDocument.Parse(argumentsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Tool arguments must be a JSON object.");
        foreach (var property in document.RootElement.EnumerateObject())
            arguments[property.Name] = property.Value.Clone();
        return arguments;
    }

    private static string Error(string message) => new JsonObject { ["error"] = message }.ToJsonString();
}
