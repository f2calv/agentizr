namespace CasCap.Client.Models;

/// <summary>Describes one caller application permitted to run an app's agents.</summary>
public sealed record CallerApplicationResponse
{
    /// <summary>Gets the caller application identifier from the validated token.</summary>
    /// <example>00000000-0000-0000-0000-000000000000</example>
    public required string Id { get; init; }

    /// <summary>Gets the configured display name, or <see langword="null" /> when none is configured.</summary>
    /// <example>Tutor voice client</example>
    public string? Name { get; init; }
}
