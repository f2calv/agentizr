using System.ComponentModel;

namespace CasCap.Models;

/// <summary>Tenant-local date and time exposed to top-level agents.</summary>
public sealed record AgentRuntimeDateTimeState
{
    /// <summary>Gets the current local date and time.</summary>
    [Description("Local date and time in the tenant time zone.")]
    public required DateTime LocalTime { get; init; }

    /// <summary>Gets the local day of the week.</summary>
    [Description("Day of the week, for example Monday.")]
    public required string DayOfWeek { get; init; }

    /// <summary>Gets the tenant time zone's UTC offset.</summary>
    [Description("UTC offset of the tenant time zone, for example +02:00:00.")]
    public required string UtcOffset { get; init; }

    /// <summary>Gets the resolved time-zone identifier.</summary>
    [Description("IANA or platform time-zone identifier.")]
    public required string TimeZone { get; init; }
}
