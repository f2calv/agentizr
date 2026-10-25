using Serilog.Events;
using System.Text.Json.Serialization;

namespace CasCap.RuntimeConfiguration.Models;

/// <summary>Typed, non-secret runtime controls for the host logging pipeline.</summary>
public sealed class RuntimeLoggingControls
{
    /// <summary>Gets or sets the pipeline minimum; category overrides remain deployment-owned.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<LogEventLevel>))]
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;
}
