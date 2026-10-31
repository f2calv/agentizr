using CasCap.Common.Exceptions;

namespace CasCap.Exceptions;

/// <summary>Indicates that a session mutation conflicts with a stateless definition.</summary>
internal sealed class StatelessAgentSessionException()
    : GenericException("Stateless agents do not support session mutations or runtime overrides.");
