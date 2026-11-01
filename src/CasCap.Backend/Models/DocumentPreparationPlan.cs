namespace CasCap.Models;

/// <summary>Admission-time preparation authorization; later configuration or active-pointer changes cannot retarget this job.</summary>
internal sealed record DocumentPreparationPlan
{
    /// <summary>Gets the tenant resolved from the trusted source at admission.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the complete credential-free immutable definition.</summary>
    public required AgentDefinition Definition { get; init; }

    /// <summary>Gets the digest of the published snapshot, before request-specific page constraints.</summary>
    public required string DefinitionSha256 { get; init; }

    /// <summary>Gets the approved preparation settings and declared model revision.</summary>
    public required DocumentAgentPreparationConfig Settings { get; init; }
}
