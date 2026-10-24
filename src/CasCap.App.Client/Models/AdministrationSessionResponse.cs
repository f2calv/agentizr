namespace CasCap.Client.Models;

/// <summary>Describes the browser's administration session without exposing identity tokens.</summary>
public sealed record AdministrationSessionResponse
{
    /// <summary>Gets whether the host requires administrator sign-in.</summary>
    /// <remarks>When <see langword="false" />, the host is an open Development instance.</remarks>
    /// <example>true</example>
    public bool SignInRequired { get; init; }

    /// <summary>Gets whether the browser holds a signed-in administrator session.</summary>
    /// <example>true</example>
    public bool IsAuthenticated { get; init; }

    /// <summary>Gets the signed-in administrator's display name.</summary>
    /// <example>Example Administrator</example>
    public string? DisplayName { get; init; }

    /// <summary>Gets the request token to send in the antiforgery header or logout form.</summary>
    /// <example>CfDJ8example</example>
    public required string AntiforgeryToken { get; init; }
}
