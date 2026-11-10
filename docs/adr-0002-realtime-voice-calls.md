# ADR 0002: Broker And Control Realtime Voice Calls In Agentizr

**Status:** Proposed.
**Date:** 2026-11-10.
**Scope:** Live speech-to-speech agent calls: agent definition, call setup, server-side session
control, tool execution, authorization and call lifecycle. Client media capture and playback belong
to consuming applications.
**Canonical record:** This document.

## Context And Problem

Agentizr currently runs text turns. A caller posts a message to
`/api/v1/agents/{agentName}/runs` or `/runs/stream`, and the runtime owns the definition, provider,
session state and MCP tools ([AgentRuntimeRoutes](../src/CasCap.AgentRuntime.Contracts/V1/Constants/AgentRuntimeRoutes.cs),
[CommonAgentExecutor](../src/CasCap.Backend/Services/CommonAgentExecutor.cs)). Voice reaches it only
as audio transcribed by a consuming application.

The first consumer is a language tutor. It wants a continuous, interruptible voice conversation
with Azure OpenAI `gpt-realtime`, while keeping the tutor's instructions and learner tools: see
[tutorizr issue #8](https://github.com/f2calv/tutorizr/issues/8). A Windows proof of concept
([tutorizr PR #9](https://github.com/f2calv/tutorizr/pull/9)) proved the realtime model and
turn-detection behavior. It connected the client directly to Azure with the user's own Entra
identity, a local placeholder prompt and no tools. That is acceptable for one developer, but it
bypasses every responsibility Agentizr owns:

- definitions and their single teaching prompt;
- provider endpoints and credentials, held behind [IProviderCredentialStore](../src/CasCap.Backend/Abstractions/IProviderCredentialStore.cs);
- MCP endpoints and credentials, held behind [ConfigurationMcpCredentialStore](../src/CasCap.Backend/Services/ConfigurationMcpCredentialStore.cs);
- tenant identity from validated claims ([AuthenticatedTenantContext](../src/CasCap.Backend/Services/AuthenticatedTenantContext.cs)),
  which already accepts delegated user tokens as well as application tokens through
  [AgentRuntimeAuthorization](../src/CasCap.Backend/Services/AgentRuntimeAuthorization.cs).

Azure documents a WebRTC flow where a service proxies the SDP negotiation, so the browser never
holds the ephemeral key. The service then attaches a server-side WebSocket to the same call using
the returned call identifier, and can issue `session.update` and other commands
([Realtime API via WebRTC](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/realtime-audio-webrtc)).
The `webrtcfilter=on` query parameter restricts the client data channel to speech, playback and
transcript events, which keeps instructions private. Microsoft recommends WebRTC rather than
WebSockets for client audio because it handles codecs, jitter and packet loss
([Realtime API via WebSockets](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/realtime-audio-websockets)).

## Goals And Constraints

- **G1** The agent definition is the single owner of instructions, realtime model settings and the
  tools a call may use. Clients never supply or see them.
- **G2** Provider and MCP credentials stay server-side; the client holds only its own Agentizr bearer token.
- **G3** Agentizr executes every tool call through its existing MCP path. It enforces the definition's
  allowlist and handles repeated deliveries of one call without re-running non-idempotent tools.
- **G4** Audio flows directly between the client and Azure. Agentizr never relays media.
- **G5** Every call ends deterministically: on client request, maximum duration, controller loss or
  host shutdown. A call never continues without a controller.
- **G6** No SDP, ephemeral keys, Azure call identifiers, transcripts or tool payloads in logs,
  metrics or traces.
- Non-goals for this decision: sharing conversation state between realtime calls and text sessions,
  persisting transcripts, SIP telephony, and providers other than Azure OpenAI.
- Backward compatibility is not required. Contracts, client methods and the definition schema may
  change in place; every consumer is upgraded together.

## Options Considered

| Option | Benefits | Costs and risks | Assessment |
| --- | --- | --- | --- |
| Keep realtime out of Agentizr (client connects directly, as in the proof of concept) | No runtime change; already works | Duplicates prompt, provider and tool ownership in every client; needs a resource role per user; no server authority | Rejected beyond the proof of concept |
| Client-side tool relay: Agentizr mints an ephemeral key with the session baked in; client connects and forwards tool calls to Agentizr | No long-lived server connection | Client holds a bearer key and can change instructions and tools with `session.update`; tool policy depends on client cooperation | Rejected: weak authority |
| Media relay: client WebSocket to Agentizr, which holds the Azure WebSocket | Full server authority; reuses the proof-of-concept client | Every call's audio passes through Agentizr, adding a hop, bandwidth and buffering; application-level echo handling and resampling | Rejected: unnecessary media path |
| **Brokered WebRTC with a server controller**: Agentizr negotiates SDP and holds the control WebSocket; media flows client to Azure | Server owns configuration and tools; no media relay; WebRTC provides codecs, jitter control and browser echo cancellation | A long-lived connection per call in one replica; clients need a WebRTC engine | **Proposed** |

## Proposed Decision

Add realtime calls to Agentizr as a separate execution mode of an agent, brokered over WebRTC with
a server-side controller.

### Definition

The agent definition gains an optional `realtime` section. It is additive, so the definition schema
version stays at 1. The section holds:

- a logical provider name and deployment;
- voice, turn detection settings, and transcription model and language;
- maximum call duration;
- a tool allowlist naming tools from the agent's existing MCP servers.

Instructions are not duplicated: a call uses the definition's instructions. An agent without the
section rejects realtime calls.

### Contract (illustrative names)

- `POST /api/v1/agents/{agentName}/realtime/calls` with a JSON body carrying the SDP offer.
  Returns `201 Created` with the SDP answer and an Agentizr-issued opaque call id, plus a
  `Location` for ending the call. JSON keeps the endpoint consistent with the rest of the API and
  its validation problem details. The Azure call id is never returned.
- `DELETE /api/v1/agents/{agentName}/realtime/calls/{callId}` ends the call.
- A dedicated permission, `agent.realtime`, granted as a delegated scope or application role and
  checked like the existing `agent.execute`. The tenant comes from the validated `azp` mapping,
  never from the request.
- Matching `IAgentRuntimeClient` methods in [the typed client](../src/CasCap.AgentRuntime.Client/Abstractions/IAgentRuntimeClient.cs).

### Call Setup

1. Resolve the tenant, the active definition and its `realtime` section. Resolve the Azure endpoint
   and credential through the provider credential store.
2. Discover the allowlisted tools from the agent's MCP servers and build the full session
   configuration: instructions, audio formats, voice, turn detection, transcription and tool schemas.
3. Mint an ephemeral key with `POST /openai/v1/realtime/client_secrets`, then post the client's offer
   to `/openai/v1/realtime/calls?webrtcfilter=on`. The ephemeral key never leaves the server.
4. Read the Azure call id from `Location` and open the control WebSocket at
   `/openai/v1/realtime?call_id=` with Agentizr's own Entra identity.
5. Record the call, then return the SDP answer.

### Controller

One controller per call owns the control WebSocket.

- It applies the session configuration, so a client's own `session.update` is overwritten at
  controller readiness. With the event filter on, the client sees no instructions.
- It executes `response.function_call_arguments.done` through the same MCP client path that text
  runs use ([AgentExtensions.Mcp](../src/CasCap.AgentRuntime.Execution/Extensions/AgentExtensions.Mcp.cs)).
  It rejects tools outside the allowlist and returns the result as a `function_call_output` item
  followed by `response.create`.
- It records each handled tool call id and does not re-execute a repeated id.
- It hangs up when the client ends the call, at the maximum duration, on host shutdown, or when the
  control connection or MCP access fails.

### Ownership And Replicas

Agentizr is otherwise stateless between requests ([High Availability](high-availability.md)). A call
controller is not: its WebSocket lives in one pod.

- A call record in PostgreSQL holds tenant, agent, Agentizr call id, owning instance, status and
  expiry, so any replica can find a call.
- The owner renews a lease on the record. `DELETE` on any replica marks the call ending; the owner
  observes that and hangs up.
- A sweeper on any replica ends calls whose lease expired, which covers a lost owner.
- Shutdown hangs up owned calls within the termination grace period.

### Session Isolation

Realtime calls do not take the text session lock and do not change text session history. The
consumer passes a session id dedicated to voice. Sharing context between voice and text is deferred.

## Consequences

- One prompt, one tool policy and one credential store serve both text and voice. Consumers need
  only a WebRTC engine and a token for Agentizr.
- Agentizr gains its first long-lived outbound connection per request. That introduces lease, sweep
  and shutdown behavior that text runs do not need, and call capacity is bounded per replica.
- Agentizr's identity needs a data-plane role on each realtime Azure OpenAI resource; client users no
  longer do.
- A client could still send events such as `response.create` or `input_audio_buffer.clear` over its
  data channel. That affects only its own conversation; it cannot add tools, change instructions
  after controller readiness or reach credentials.
- Tool latency adds one Agentizr-to-MCP round trip inside a response, which is the same path text
  runs use.
- Consumers of the earlier direct proof of concept must replace their WebSocket audio client with
  WebRTC; no compatibility path is kept.

## Validation And Delivery

Acceptance evidence, not yet observed:

- [x] Unit tests for session configuration, allowlist refusal, repeated tool call ids, argument
  validation and provider brokering against a fake HTTP handler
  ([RealtimeCallTests](../src/CasCap.Backend.Tests/Tests/Unit/RealtimeCallTests.cs), 10 passing locally).
- [ ] Controller-level tests: setup authorization, the definition without a `realtime` section, maximum
  duration and hang-up paths, with a fake Azure realtime endpoint.
- [ ] Integration test on the local Compose services: call record, lease renewal, `DELETE` from a
  non-owning instance and expired-lease sweep.
- [ ] Live smoke from the tutor's Windows client: instructions applied without client knowledge, one
  read tool and one write tool executed, interruption and hang-up on End and on duration expiry.
- [ ] Log and telemetry review showing none of the data in G6.

Delivery order:

1. Contracts, definition schema and typed client.
2. Setup service and controller with offline tests.
3. Call record, lease and sweeper.
4. Tutor client migration to WebRTC, then live validation.
5. Release.

## Handover

- Branch `f2calv/2026-11-realtime-calls` (local, from `main`) holds this record and the first slice:
  contracts, typed client, the `realtime` definition section, setup service, in-memory controller
  registry and unit tests. Delivery steps 1 and 2 are implemented; step 3 (leased call record and
  sweeper) is not, so realtime calls currently require a single runtime replica.
- Entra and Azure changes are prepared as a separate operator handover, not executed by this work:
  a public client registration for the tutor app, a realtime scope on the Agentizr API, the
  `azp` tenant mapping, and a data-plane role for Agentizr's identity on the realtime resource.
- The .NET 11 migration branch is pending GA; this work targets the current `main`.

## Open Questions And Reconsideration

- Does Azure expose a server hang-up operation for a WebRTC call, or must the controller close its
  WebSocket and rely on Azure ending the call? This changes how a non-owning replica ends a call.
- Can a second control WebSocket attach to an existing call after the owner is lost? If so, a
  replica could take over a call instead of ending it.
- How long do ephemeral keys and calls live, and what does Azure enforce? Verify on the deployed
  API rather than assuming documented examples.
- Reconsider media relay if a client platform cannot run WebRTC, or if network policy blocks direct
  client access to Azure media.
- Reconsider session isolation when voice and text should continue one conversation.

## Evidence And Related Decisions

| Evidence | Relevance |
| --- | --- |
| [Realtime API via WebRTC](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/realtime-audio-webrtc) | Ephemeral keys, proxied SDP, `Location` call id, control WebSocket, `webrtcfilter` |
| [Realtime API via WebSockets](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/realtime-audio-websockets) | GA endpoint, Entra scope and session configuration shape |
| [AuthenticatedTenantContext](../src/CasCap.Backend/Services/AuthenticatedTenantContext.cs) | Tenant from the `azp` claim mapping |
| [AgentRuntimeAuthorization](../src/CasCap.Backend/Services/AgentRuntimeAuthorization.cs) | Delegated scopes and application roles |
| [AgentDefinitionSnapshotEntity](../src/CasCap.Data/Entities/AgentDefinitionSnapshotEntity.cs) | Schema-versioned definition snapshots |
| [High Availability](high-availability.md) | Current stateless replica model this decision extends |
| [tutorizr issue #8](https://github.com/f2calv/tutorizr/issues/8) and [PR #9](https://github.com/f2calv/tutorizr/pull/9) | Consumer requirements and the direct proof of concept |
| [ADR 0001](adr-0001-centralized-document-rag.md) | Precedent for centralizing a capability in Agentizr |
