namespace CasCap.Models;

/// <summary>Operator-configured token prices for one realtime deployment, used only to estimate call cost for debugging.</summary>
/// <remarks>Copy prices from the provider's price list for the deployment's model and region; nothing here is fetched.</remarks>
public sealed record RealtimeTokenPricing
{
    /// <summary>Gets the ISO 4217 currency code of every price below.</summary>
    [Required, RegularExpression("^[A-Z]{3}$")]
    public required string Currency { get; init; }

    /// <summary>Gets the price per million uncached input text tokens.</summary>
    [Range(0, 10_000)]
    public decimal TextInputPerMillion { get; init; }

    /// <summary>Gets the price per million cached input text tokens.</summary>
    [Range(0, 10_000)]
    public decimal CachedTextInputPerMillion { get; init; }

    /// <summary>Gets the price per million uncached input audio tokens.</summary>
    [Range(0, 10_000)]
    public decimal AudioInputPerMillion { get; init; }

    /// <summary>Gets the price per million cached input audio tokens.</summary>
    [Range(0, 10_000)]
    public decimal CachedAudioInputPerMillion { get; init; }

    /// <summary>Gets the price per million output text tokens.</summary>
    [Range(0, 10_000)]
    public decimal TextOutputPerMillion { get; init; }

    /// <summary>Gets the price per million output audio tokens.</summary>
    [Range(0, 10_000)]
    public decimal AudioOutputPerMillion { get; init; }
}
