namespace CasCap.Models;

/// <summary>States which definition version an administrator expects to be active when changing it.</summary>
/// <param name="Version">The expected active version, or <see langword="null" /> when none should be active.</param>
public sealed record ExpectedActiveDefinition(string? Version);
