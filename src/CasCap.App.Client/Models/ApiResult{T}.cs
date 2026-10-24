namespace CasCap.Client.Models;

/// <summary>The typed outcome of an administration API call.</summary>
/// <typeparam name="T">The response body type.</typeparam>
/// <param name="Status">The outcome classification.</param>
/// <param name="Value">The response body on success.</param>
/// <param name="Errors">Validation or problem messages returned by the server.</param>
public sealed record ApiResult<T>(ApiStatus Status, T? Value = default, IReadOnlyList<string>? Errors = null)
{
    /// <summary>Gets whether the call succeeded.</summary>
    public bool IsSuccess => Status == ApiStatus.Success;
}
