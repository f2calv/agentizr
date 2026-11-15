namespace CasCap.Models;

/// <summary>Materializes native exact cosine ranking without exposing vector values.</summary>
internal sealed record DocumentKnowledgeMatch(string JobId, int Ordinal, double Distance);
