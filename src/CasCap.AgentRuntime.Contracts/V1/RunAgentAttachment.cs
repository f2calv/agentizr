namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Represents a binary attachment produced by an agent tool.</summary>
public sealed record RunAgentAttachment
{
    /// <summary>Gets the MIME type.</summary>
    /// <example>image/jpeg</example>
    public required string MimeType { get; init; }

    /// <summary>Gets the optional display filename.</summary>
    /// <example>photo.jpg</example>
    public string? FileName { get; init; }

    /// <summary>Gets the Base64-encoded attachment content.</summary>
    /// <example>/9j/4AAQSkZJRg...</example>
    public required string Base64Content { get; init; }
}
