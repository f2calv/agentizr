namespace CasCap.Models;

/// <summary>The instructions the next run of one agent session will use.</summary>
/// <param name="DefinitionVersion">The active definition version.</param>
/// <param name="Overridden">Whether a per-session override replaces the definition instructions.</param>
/// <param name="Instructions">The effective instructions.</param>
public sealed record AgentInstructionsView(string DefinitionVersion, bool Overridden, string Instructions);
