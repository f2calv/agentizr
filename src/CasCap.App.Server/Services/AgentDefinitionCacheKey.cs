using System.Security.Cryptography;
using System.Text;

namespace CasCap.Services;

/// <summary>Builds opaque Redis keys for immutable tenant agent definition versions.</summary>
internal static class AgentDefinitionCacheKey
{
    /// <summary>Creates a version-qualified definition cache key.</summary>
    public static string Create(string tenantId, string agentName, string definitionVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionVersion);
        var source = $"{tenantId.Length}:{tenantId}{agentName.Length}:{agentName}"
            + $"{definitionVersion.Length}:{definitionVersion}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        return $"agentizr:v1:definition:version:{digest}";
    }
}
