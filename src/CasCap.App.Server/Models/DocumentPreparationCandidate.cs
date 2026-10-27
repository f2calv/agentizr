namespace CasCap.Models;

/// <summary>Locally validated page transcription and review notes; none establishes semantic fidelity.</summary>
internal sealed record DocumentPreparationCandidate(int Page, string Markdown, string[] Changes, string[] Uncertainties);
