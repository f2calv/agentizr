using System.Text.Json;

namespace CasCap.Client.Models;

/// <summary>A secret-free starting point for an explicitly published tenant-local definition.</summary>
public sealed record BuiltInDefinitionTemplateResponse
{
    /// <summary>Gets the stable suggested agent key.</summary>
    /// <example>document-preparation</example>
    public required string AgentName { get; init; }

    /// <summary>Gets the version of the shipped instructions and output contract.</summary>
    /// <example>PdfReconciliationV1</example>
    public required string TemplateVersion { get; init; }

    /// <summary>Gets the editable agent/provider document. Provider credentials and deployment coordinates are absent.</summary>
    /// <example>{"agent":{},"provider":{}}</example>
    public required JsonElement Definition { get; init; }
}
