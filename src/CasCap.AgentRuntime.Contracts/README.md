# CasCap.AgentRuntime.Contracts

Versioned request and response DTOs for the CasCap multi-tenant Agent Runtime HTTP protocol.

## Version 1

`RunAgentRequest` carries a caller-owned session identifier and input. The agent name is part of the
route, and tenant identity deliberately does not appear in the body: the server derives it from the
authenticated request context.

`RunAgentResponse` returns output, the session identifier, and the definition version used for the
turn.
