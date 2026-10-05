# CasCap.AgentRuntime.Client

Typed HTTP client for the CasCap multi-tenant Agent Runtime.

Register it with `AddAgentRuntimeClient()` and configure
`CasCap:AgentRuntimeClientOptions:BaseAddress`. Authentication belongs to the consuming application:
attach the required credentials or identity through the configured `HttpClient` pipeline.

`RunAgentAsync` returns `null` when the named agent is unavailable to the authenticated tenant and
throws for other non-success responses.
