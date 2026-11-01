namespace CasCap.Client.Models;

/// <summary>Editable fields of a definition draft, validated in the browser for usability and again by the server.</summary>
public sealed class DefinitionForm
{
    /// <summary>Gets or sets the stable agent key used by routes, storage and delegation.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    [Display(Name = "Agent name")]
    public string AgentName { get; set; } = string.Empty;

    /// <summary>Gets or sets the new immutable version identifier.</summary>
    [Required, StringLength(100, MinimumLength = 1)]
    [Display(Name = "New version")]
    public string DefinitionVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the agent display name.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the agent description.</summary>
    [Required]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the default prompt sent with image or event data.</summary>
    [Required]
    [Display(Name = "Default prompt")]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Gets or sets the inline system instructions.</summary>
    [Required]
    [Display(Name = "Instructions")]
    public string Instructions { get; set; } = string.Empty;

    /// <summary>Gets or sets the provider registration name that supplies credentials at run time.</summary>
    [Required]
    [Display(Name = "Provider registration")]
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Gets or sets the provider type name embedded in the version.</summary>
    [Required]
    [Display(Name = "Provider type")]
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>Gets or sets the provider endpoint embedded in the version; optional only for OpenAI.</summary>
    [Url]
    [Display(Name = "Endpoint")]
    public string? Endpoint { get; set; }

    /// <summary>Gets or sets the model embedded in the version.</summary>
    [Required]
    [Display(Name = "Model")]
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Gets or sets the reasoning effort name, or <see langword="null" /> for none.</summary>
    [Display(Name = "Reasoning effort")]
    public string? ReasoningEffort { get; set; }

    /// <summary>Gets or sets whether to use the Responses API rather than Chat Completions.</summary>
    public bool UseResponsesApi { get; set; }

    /// <summary>Gets or sets the tool sources as a JSON array.</summary>
    [Required]
    [Display(Name = "Tools")]
    public string ToolsJson { get; set; } = "[]";

    /// <summary>Gets or sets the publication reason recorded with the version.</summary>
    [Required, StringLength(2_000, MinimumLength = 1)]
    [Display(Name = "Publication reason")]
    public string ChangeReason { get; set; } = string.Empty;
}
