using System.Security.Cryptography;
using System.Text;

namespace CasCap.Services;

/// <summary>Builds collision-free state keys from tenant, agent, and session identifiers.</summary>
internal static class AgentStateKey
{
    /// <summary>Creates a length-prefixed composite key.</summary>
    public static string Create(string tenantId, string agentName, string definitionVersion, string sessionId)
        => CreateComposite(tenantId, agentName, definitionVersion, sessionId);

    /// <summary>Creates a length-prefixed composite snapshot key.</summary>
    public static string CreateSnapshot(
        string tenantId,
        string agentName,
        string definitionVersion,
        string sessionId,
        string snapshotName) =>
        CreateComposite(tenantId, agentName, definitionVersion, sessionId, snapshotName);

    /// <summary>Creates an opaque namespaced Redis key that does not disclose its identifiers.</summary>
    public static string CreateRedis(
        string stateKind,
        string tenantId,
        string agentName,
        string definitionVersion,
        string sessionId) =>
        CreateRedis(stateKind, Create(tenantId, agentName, definitionVersion, sessionId));

    /// <summary>Creates an opaque namespaced Redis snapshot key that does not disclose its identifiers.</summary>
    public static string CreateRedisSnapshot(
        string stateKind,
        string tenantId,
        string agentName,
        string definitionVersion,
        string sessionId,
        string snapshotName) =>
        CreateRedis(stateKind, CreateSnapshot(tenantId, agentName, definitionVersion, sessionId, snapshotName));

    private static string CreateComposite(params string[] values)
    {
        foreach (var value in values)
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var builder = new StringBuilder(values.Sum(value => value.Length) + values.Length * 8);
        foreach (var value in values)
            Append(builder, value);
        return builder.ToString();
    }

    private static string CreateRedis(string stateKind, string composite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateKind);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(composite)));
        return $"agentizr:v1:{stateKind}:{digest}";
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value);
}
