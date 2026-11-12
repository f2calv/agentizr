namespace CasCap.Client.Models;

/// <summary>Summarizes one app, the tenant boundary an administrator manages.</summary>
public sealed record AppSummaryResponse
{
    /// <summary>Gets the stable app identifier used in administration routes.</summary>
    /// <example>contoso</example>
    public required string AppId { get; init; }

    /// <summary>Gets the caller applications permitted to run this app's agents.</summary>
    public required IReadOnlyList<CallerApplicationResponse> CallerApplications { get; init; }

    /// <summary>Gets how many agents have at least one published definition.</summary>
    /// <example>3</example>
    public int AgentCount { get; init; }
}
