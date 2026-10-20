# CasCap.AgentRuntime.Contracts

Versioned request and response DTOs for .NET 10 applications calling the CasCap multi-tenant Agent
Runtime HTTP API. The contracts carry execution, conversation-state and definition-administration
messages without trusting caller-supplied tenant or actor identity.

## Installation

Reference this project directly, or use `CasCap.AgentRuntime.Client`, which references it.
The packable project targets `net10.0` with package ID
`CasCap.AgentRuntime.Contracts`; a public NuGet listing is not currently available.

## Usage

Types live in `CasCap.AgentRuntime.Contracts.V1`. A minimal run request is:

```csharp
using CasCap.AgentRuntime.Contracts.V1;

var request = new RunAgentRequest
{
    SessionId = "conversation-example",
    Input = "Summarize the available capabilities.",
};
```

Send it to `POST /api/v1/agents/{agentName}/runs` through an authenticated HTTP client.

## Version 1

[`RunAgentRequest`](https://github.com/f2calv/agentizr/blob/13aa4a19180b9dbd0f14fa08fbf95f67039b69f0/src/CasCap.AgentRuntime.Contracts/V1/RunAgentRequest.cs)
carries a caller-owned session identifier, text, optional binary input with its
MIME type, and one-turn session bypass. The agent name is part of the route, and tenant identity
deliberately does not appear in the body: the server derives it from the authenticated request
context.

[`RunAgentResponse`](https://github.com/f2calv/agentizr/blob/13aa4a19180b9dbd0f14fa08fbf95f67039b69f0/src/CasCap.AgentRuntime.Contracts/V1/RunAgentResponse.cs)
returns output, definition/model identity, timing, provider usage, tool-call
names, tool-produced attachments, and structured delegation/compaction events. Tool arguments and
provider/host property bags are returned only when the authenticated caller explicitly enables
diagnostic details. Session summaries expose sizes and message counts without raw state.
`SessionId` accepts 1–200 characters and `Input` 1–100,000. `BinaryContent` is JSON Base64,
accepts at most 20 MiB decoded, and must be paired with `MimeType`.

[`RunAgentStreamItem`](https://github.com/f2calv/agentizr/blob/13aa4a19180b9dbd0f14fa08fbf95f67039b69f0/src/CasCap.AgentRuntime.Contracts/V1/RunAgentStreamItem.cs)
carries delegation and compaction events as they occur, followed by the final
`RunAgentResponse`, so callers can update progress indicators without owning runtime execution.
The streaming endpoint is `POST /api/v1/agents/{agentName}/runs/stream` and returns an incremental
JSON array rather than Server-Sent Events.

Session-control contracts inspect and reset active state, compact history, manage named snapshots,
and replace complete per-session model, instruction, and persistence overrides. They expose
structured summaries rather than raw Agent Framework session JSON.
[`AgentInstructionsResponse`](https://github.com/f2calv/agentizr/blob/13aa4a19180b9dbd0f14fa08fbf95f67039b69f0/src/CasCap.AgentRuntime.Contracts/V1/AgentInstructionsResponse.cs)
reports the effective instructions and whether an override supplies them.

Definition-management contracts publish immutable schema-versioned JSON documents, inspect snapshot
and activation history, and activate or roll back versions separately. Tenant and actor identity
never appear in request DTOs; the server derives both from authenticated claims.

[`EvaluateAgentDefinitionRequest`](https://github.com/f2calv/agentizr/blob/13aa4a19180b9dbd0f14fa08fbf95f67039b69f0/src/CasCap.AgentRuntime.Contracts/V1/EvaluateAgentDefinitionRequest.cs)
accepts 1–50 scenarios, 1–10 repetitions (default `1`), and a
`MinimumPassRate` from `0` to `1` (default `1`). Each scenario supplies answer expectations,
required/forbidden tool names and optional fixture responses. Activation can carry this request as
[`ActivateAgentDefinitionRequest.Evaluation`](https://github.com/f2calv/agentizr/blob/13aa4a19180b9dbd0f14fa08fbf95f67039b69f0/src/CasCap.AgentRuntime.Contracts/V1/ActivateAgentDefinitionRequest.cs);
deactivation is a separate reason-bearing request.

## Documentation

- [Endpoint and permission reference](https://github.com/f2calv/agentizr/blob/main/src/CasCap.App.Server/README.md#public-surface)
- [Runtime source and project catalogue](https://github.com/f2calv/agentizr)
- [License](https://github.com/f2calv/agentizr/blob/main/LICENSE)
