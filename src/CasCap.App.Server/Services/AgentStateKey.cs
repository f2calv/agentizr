using System.Text;

namespace CasCap.Services;

/// <summary>Builds collision-free process-local keys from tenant, agent, and session identifiers.</summary>
internal static class AgentStateKey
{
    /// <summary>Creates a length-prefixed composite key.</summary>
    public static string Create(string tenantId, string agentName, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var builder = new StringBuilder(tenantId.Length + agentName.Length + sessionId.Length + 24);
        Append(builder, tenantId);
        Append(builder, agentName);
        Append(builder, sessionId);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value);
}
