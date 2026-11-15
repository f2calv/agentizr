namespace CasCap.Models;

/// <summary>Captures a current published manifest and its reviewed learning level under the mutation fence.</summary>
internal sealed record DocumentKnowledgeGeneration(string JobId, DocumentIndexingManifest Manifest, string CefrLevel);
