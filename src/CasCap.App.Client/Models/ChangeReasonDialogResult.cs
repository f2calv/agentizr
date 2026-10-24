namespace CasCap.Client.Models;

/// <summary>The confirmed reason and optional evaluation from a change confirmation dialog.</summary>
/// <param name="Reason">The audited reason.</param>
/// <param name="Evaluation">Fixture scenarios that must pass before activation, when supplied.</param>
public sealed record ChangeReasonDialogResult(string Reason, EvaluateAgentDefinitionRequest? Evaluation);
