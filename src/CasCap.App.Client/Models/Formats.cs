namespace CasCap.Client.Models;

/// <summary>Formats values for display in the administrator's browser time zone.</summary>
public static class Formats
{
    /// <summary>Formats a UTC instant as local date and time, or a dash when absent.</summary>
    public static string DateTime(DateTimeOffset? utc) =>
        utc is { } value ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "—";
}
