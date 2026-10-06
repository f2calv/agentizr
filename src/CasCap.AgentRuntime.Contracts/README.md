# CasCap.AgentRuntime.Contracts

Versioned request and response DTOs for the CasCap multi-tenant Agent Runtime HTTP protocol.

## Version 1

`RunAgentRequest` carries a caller-owned session identifier, text, optional binary input with its
MIME type, and one-turn session bypass. The agent name is part of the route, and tenant identity
deliberately does not appear in the body: the server derives it from the authenticated request
context.

`RunAgentResponse` returns output, definition/model identity, timing, provider usage, tool-call
names, tool-produced attachments, and structured delegation/compaction events. Tool arguments and
tenant content are deliberately omitted from diagnostics.

Session-control contracts inspect and reset active state, compact history, manage named snapshots,
and replace complete per-session model, instruction, and persistence overrides. They expose
structured summaries rather than raw Agent Framework session JSON.
