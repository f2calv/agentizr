# CasCap.AgentRuntime.Client

Typed HTTP client for the CasCap multi-tenant Agent Runtime.

Register it with `AddAgentRuntimeClient()` and configure
`CasCap:AgentRuntimeClientOptions:BaseAddress` and optional `TimeoutMinutes`. The registration returns
an `IHttpClientBuilder`; authentication and resilience belong to the consuming application and are
attached through that pipeline.

`RunAgentAsync` returns `null` when the named agent is unavailable to the authenticated tenant and
throws for other non-success responses. Session methods inspect/reset/compact active state, manage
named snapshots, and get or replace complete per-session runtime overrides without exposing raw
session JSON.

`StreamAgentAsync` incrementally yields live delegation/compaction events and then the final response
using a streamed JSON response. Callers opt into tool arguments and property bags through
`RunAgentRequest.IncludeDiagnosticDetails` only when their diagnostic destination is operator-controlled.

Register `IAgentDefinitionAdminClient` separately with `AddAgentDefinitionAdminClient()`. It publishes
inactive immutable snapshots, reads active/version/history state, activates or rolls back versions,
and reads activation audit history. Attach control-plane credentials carrying definition scopes to
that client; execution workloads need not receive those permissions.
