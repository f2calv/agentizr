namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Requests one turn against a named agent and caller-owned session.</summary>
public sealed record RunAgentRequest : IValidatableObject
{
    /// <summary>Gets the caller-owned session identifier.</summary>
    /// <example>conversation-42</example>
    [Required, MinLength(1), MaxLength(200)]
    public required string SessionId { get; init; }

    /// <summary>Gets the user input for this turn.</summary>
    /// <example>Summarize the latest system status.</example>
    [Required, MinLength(1), MaxLength(100_000)]
    public required string Input { get; init; }

    /// <summary>Gets optional binary input encoded by JSON as Base64.</summary>
    [MaxLength(20 * 1024 * 1024)]
    public byte[]? BinaryContent { get; init; }

    /// <summary>Gets the MIME type paired with <see cref="BinaryContent" />.</summary>
    /// <example>image/jpeg</example>
    [MaxLength(200)]
    public string? MimeType { get; init; }

    /// <summary>Gets whether this turn bypasses session loading and persistence.</summary>
    public bool BypassSession { get; init; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((BinaryContent is null) != string.IsNullOrWhiteSpace(MimeType))
            yield return new ValidationResult(
                "BinaryContent and MimeType must be supplied together.",
                [nameof(BinaryContent), nameof(MimeType)]);
    }
}
