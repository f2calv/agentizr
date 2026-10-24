namespace CasCap.Client.Models;

/// <summary>Classifies the outcome of an administration API call so pages can render each state explicitly.</summary>
public enum ApiStatus
{
    /// <summary>The request succeeded.</summary>
    Success,

    /// <summary>The server rejected the request with validation errors.</summary>
    ValidationFailed,

    /// <summary>The browser has no administrator session.</summary>
    Unauthorized,

    /// <summary>The administrator is not assigned to the requested app.</summary>
    Forbidden,

    /// <summary>The requested resource does not exist.</summary>
    NotFound,

    /// <summary>Another change happened first, such as a different active version or an existing version.</summary>
    Conflict,

    /// <summary>The server is unreachable or does not support the operation in its current configuration.</summary>
    Unavailable,

    /// <summary>The request failed for another reason.</summary>
    Failed,
}
