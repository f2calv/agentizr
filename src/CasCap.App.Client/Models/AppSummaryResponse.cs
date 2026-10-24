namespace CasCap.Client.Models;

/// <summary>Summarizes one app, the tenant boundary an administrator manages.</summary>
public sealed record AppSummaryResponse
{
    /// <summary>Gets the stable app identifier used in administration routes.</summary>
    /// <example>contoso</example>
    public required string AppId { get; init; }

    /// <summary>Gets the caller application identifiers permitted to run this app's agents.</summary>
    /// <example>["00000000-0000-0000-0000-000000000000"]</example>
    public required IReadOnlyList<string> CallerApplications { get; init; }

    /// <summary>Gets how many agents have at least one published definition.</summary>
    /// <example>3</example>
    public int AgentCount { get; init; }
}
