using CasCap.Common.Exceptions;

namespace CasCap.Exceptions;

/// <summary>Indicates that a session is busy or that its operation no longer owns the session lease.</summary>
public sealed class AgentSessionConflictException(string message) : GenericException(message);
