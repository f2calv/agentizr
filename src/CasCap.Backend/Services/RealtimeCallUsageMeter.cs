using CasCap.AgentRuntime.Contracts.V1;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Sums the token usage reported in each <c>response.done</c> event of one realtime call.</summary>
internal sealed class RealtimeCallUsageMeter
{
    private long _inputText;
    private long _inputAudio;
    private long _cachedText;
    private long _cachedAudio;
    private long _outputText;
    private long _outputAudio;

    /// <summary>Adds the usage of one completed response; responses without usage are ignored.</summary>
    /// <param name="response">The <c>response</c> object of a <c>response.done</c> event.</param>
    public void Add(JsonElement response)
    {
        if (!response.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return;
        if (usage.TryGetProperty("input_token_details", out var input))
        {
            _inputText += Read(input, "text_tokens");
            _inputAudio += Read(input, "audio_tokens");
            if (input.TryGetProperty("cached_tokens_details", out var cached))
            {
                _cachedText += Read(cached, "text_tokens");
                _cachedAudio += Read(cached, "audio_tokens");
            }
        }
        if (usage.TryGetProperty("output_token_details", out var output))
        {
            _outputText += Read(output, "text_tokens");
            _outputAudio += Read(output, "audio_tokens");
        }
    }

    /// <summary>Gets the summed usage.</summary>
    public RealtimeCallUsage ToUsage() => new()
    {
        TotalTokens = _inputText + _inputAudio + _outputText + _outputAudio,
        InputTextTokens = _inputText,
        InputAudioTokens = _inputAudio,
        CachedInputTokens = _cachedText + _cachedAudio,
        OutputTextTokens = _outputText,
        OutputAudioTokens = _outputAudio,
    };

    /// <summary>Estimates the cost of the summed usage, or returns <see langword="null"/> without prices.</summary>
    /// <param name="pricing">The deployment's configured prices, if any.</param>
    public RealtimeCallCost? EstimateCost(RealtimeTokenPricing? pricing)
    {
        if (pricing is null)
            return null;
        var amount = PerMillion(_inputText - _cachedText, pricing.TextInputPerMillion)
            + PerMillion(_cachedText, pricing.CachedTextInputPerMillion)
            + PerMillion(_inputAudio - _cachedAudio, pricing.AudioInputPerMillion)
            + PerMillion(_cachedAudio, pricing.CachedAudioInputPerMillion)
            + PerMillion(_outputText, pricing.TextOutputPerMillion)
            + PerMillion(_outputAudio, pricing.AudioOutputPerMillion);
        return new RealtimeCallCost { Amount = decimal.Round(amount, 4), Currency = pricing.Currency };
    }

    private static decimal PerMillion(long tokens, decimal price) => Math.Max(tokens, 0) * price / 1_000_000m;

    private static long Read(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var count) ? count : 0;
}
