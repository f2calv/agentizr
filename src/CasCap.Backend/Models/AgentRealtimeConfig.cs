namespace CasCap.Models;

/// <summary>Realtime voice call settings of an agent definition; the call uses the definition's instructions.</summary>
public sealed record AgentRealtimeConfig : IValidatableObject
{
    private static readonly string[] TurnDetectionTypes = ["semantic_vad", "server_vad"];
    private static readonly string[] EagernessValues = ["low", "medium", "high", "auto"];

    /// <summary>Gets the tenant provider name whose Entra identity or API key authenticates to the realtime resource.</summary>
    [Required, MinLength(1), MaxLength(200)]
    public required string Provider { get; init; }

    /// <summary>Gets the Azure OpenAI resource endpoint, without an API path.</summary>
    [Required]
    public required Uri Endpoint { get; init; }

    /// <summary>Gets the realtime model deployment name.</summary>
    [Required, MinLength(1), MaxLength(200)]
    public required string Deployment { get; init; }

    /// <summary>Gets the output voice.</summary>
    [Required, MinLength(1), MaxLength(50)]
    public string Voice { get; init; } = "marin";

    /// <summary>Gets the turn detection type: <c>semantic_vad</c> or <c>server_vad</c>.</summary>
    [Required]
    public string TurnDetection { get; init; } = "semantic_vad";

    /// <summary>Gets the semantic VAD eagerness: <c>low</c>, <c>medium</c>, <c>high</c> or <c>auto</c>.</summary>
    [Required]
    public string Eagerness { get; init; } = "auto";

    /// <summary>Gets the user-speech transcription model, or <see langword="null"/> to disable user transcripts.</summary>
    [MaxLength(200)]
    public string? TranscriptionModel { get; init; }

    /// <summary>Gets the ISO-639-1 transcription language hint.</summary>
    [MaxLength(10)]
    public string? TranscriptionLanguage { get; init; }

    /// <summary>Gets the hard ceiling on call length, counted from call start; activity does not extend it.</summary>
    [Range(1, 120)]
    public int MaximumDurationMinutes { get; init; } = 30;

    /// <summary>Gets the names of the agent's remote MCP tools that a call may execute.</summary>
    public string[] Tools { get; init; } = [];

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!TurnDetectionTypes.Contains(TurnDetection, StringComparer.Ordinal))
            yield return new ValidationResult(
                $"{nameof(TurnDetection)} must be one of: {string.Join(", ", TurnDetectionTypes)}.", [nameof(TurnDetection)]);
        if (!EagernessValues.Contains(Eagerness, StringComparer.Ordinal))
            yield return new ValidationResult(
                $"{nameof(Eagerness)} must be one of: {string.Join(", ", EagernessValues)}.", [nameof(Eagerness)]);
        if (Tools is null || Tools.Any(string.IsNullOrWhiteSpace))
            yield return new ValidationResult($"{nameof(Tools)} must contain only tool names.", [nameof(Tools)]);
    }
}
