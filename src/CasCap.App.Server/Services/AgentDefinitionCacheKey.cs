using System.Security.Cryptography;
using System.Text;

namespace CasCap.Services;

/// <summary>Builds opaque Redis keys for active tenant agent definitions.</summary>
internal static class AgentDefinitionCacheKey
{
    /// <summary>Creates the active-definition cache key.</summary>
    public static string Create(string tenantId, string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        var source = $"{tenantId.Length}:{tenantId}{agentName.Length}:{agentName}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        return $"agentizr:v1:definition:active:{digest}";
    }
}
