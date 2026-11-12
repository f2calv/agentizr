namespace CasCap.Client.Models;

/// <summary>Describes one published definition version in the administration history.</summary>
public sealed record AdministrationDefinitionVersionResponse
{
    /// <summary>Gets the immutable definition version.</summary>
    /// <example>2026-11-11.5</example>
    public required string DefinitionVersion { get; init; }

    /// <summary>Gets when the version was published.</summary>
    public DateTimeOffset PublishedAtUtc { get; init; }

    /// <summary>Gets the authenticated publisher's subject identifier.</summary>
    public string? PublishedBy { get; init; }

    /// <summary>Gets the optional publication reason.</summary>
    /// <example>Raise the voice call limit to ten minutes.</example>
    public string? ChangeReason { get; init; }

    /// <summary>Gets whether this version is active.</summary>
    public bool IsActive { get; init; }

    /// <summary>Gets whether an administrator archived this version.</summary>
    public bool IsArchived { get; init; }

    /// <summary>Gets whether this version was ever activated, which keeps it in the audit history.</summary>
    public bool WasActivated { get; init; }

    /// <summary>Gets labels of the properties changed since the previously published version.</summary>
    /// <example>["instructions", "realtime"]</example>
    public required IReadOnlyList<string> ChangedProperties { get; init; }
}
