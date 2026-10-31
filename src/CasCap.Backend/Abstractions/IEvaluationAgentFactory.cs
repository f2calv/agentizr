using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CasCap.Abstractions;

/// <summary>Creates the provider-backed agent used by a definition evaluation run.</summary>
internal interface IEvaluationAgentFactory
{
    /// <summary>Creates an agent over the supplied tools and returns it with its resolved instructions.</summary>
    public (AIAgent Agent, string Instructions) Create(ProviderConfig provider, AgentConfig agent, List<AITool> tools);
}
