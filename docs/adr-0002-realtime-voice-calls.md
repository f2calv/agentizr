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
- `DELETE /api/v1/agents/{agentName}/realtime/calls/{callId}` ends the call and returns its end reason, duration,
  token usage and an optional estimated cost; a call the runtime ended itself, for example at its maximum
  duration, returns the same summary for a short retention period so the client can explain the hang-up.
- A dedicated permission, `agent.realtime`, checked like the existing `agent.execute`. Voice clients
  act for a signed-in user, so it is granted as a delegated scope. Microsoft Entra shares one value
  namespace between delegated scopes and application roles, so a future machine caller needs a
  differently named role. The tenant comes from the validated `azp` mapping, never from the request.
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

Acceptance evidence:

- [x] Unit tests for session configuration, allowlist refusal, repeated tool call ids, argument
  validation, provider brokering against a fake HTTP handler and allowlisted runtime built-ins that
  outlive the setup request ([RealtimeCallTests](../src/CasCap.Backend.Tests/Tests/Unit/RealtimeCallTests.cs)).
- [x] Unit tests for the leased call record: tenant-scoped end requests stop the owner's renewal, an
  expired lease is claimed once, `DELETE` on a non-owning registry returns the owner's summary, and the
  sweeper hangs up an expired call with the tenant credential and records `ControlLost`
  ([RealtimeCallTests](../src/CasCap.Backend.Tests/Tests/Unit/RealtimeCallTests.cs)).
- [x] Controller-level tests against a fake Azure realtime endpoint: no `realtime` section, setup with
  the runtime credential, hang-up after lost control and an unknown call
  ([RealtimeCallProtocolTests](../src/CasCap.App.Server.Tests/Tests/Integration/RealtimeCallProtocolTests.cs));
  the `Realtime` policy is pinned by
  [TenantSecurityStartupTests](../src/CasCap.App.Server.Tests/Tests/Integration/TenantSecurityStartupTests.cs)
  and the maximum duration was verified live.
- [x] Integration test on the local Compose PostgreSQL: call record, lease renewal, `DELETE` from a
  non-owning registry, one claimant among concurrent sweepers and the expired-lease sweep
  ([PostgresRealtimeCallStoreTests](../src/CasCap.App.Server.Tests/Tests/Integration/PostgresRealtimeCallStoreTests.cs)).
- [x] Live smoke from a browser WebRTC page with a delegated token (2026-11-11): 401 without a token,
  404 for an unknown agent, instructions applied without client knowledge, read and write MCP tools
  executed by the runtime (including model recovery from a tool error), user and assistant
  transcripts on the filtered data channel, interruption, and hang-up on End.
- [x] Live smoke from the tutor's Windows client (2026-11-11): WebRTC in a hosted web view with a
  delegated token, a read tool, local mute and hang-up on End; with a 1-minute ceiling the runtime hung
  up 59 seconds after call start despite tool activity.
- [x] Live smoke with two runtime replicas (2026-11-11): three calls whose `DELETE` reached the
  non-owning replica each returned the owner's summary. The first run reported `ProviderClosed` and
  took 5 to 7 seconds because the client closed media before ending the call and the owner then waited
  out a hang-up of an already closed call; both were fixed and redeployed.
- [x] Live smoke from the tutor's Android client on the emulator (2026-11-11): sign-in through the
  application-ID redirect, settings entered on the device, microphone capture, transcripts and the
  call summary with end reason and token usage.
- [ ] Live smoke from a physical Android phone away from the private network.
- [x] Log and telemetry review for G6 (2026-11-11). Realtime log templates carry only class, tool and
  provider names, error codes, end reasons, durations and token counts; exceptions from the provider
  client carry status codes only. A failing tool logged its exception message, which can echo learner
  content, so it now logs only the exception type
  ([RealtimeCallTests](../src/CasCap.Backend.Tests/Tests/Unit/RealtimeCallTests.cs)). The runtime
  traces no outbound HTTP; `System` logging stays at `Warning`, which keeps `HttpClient` request lines,
  whose hang-up path contains the Azure call id, out of the logs. Request logging records route
  templates only. Logs from the day's live calls on both replicas contained no call ids, keys, SDP or
  transcripts.

Delivery order:

1. Contracts, definition schema and typed client.
2. Setup service and controller with offline tests.
3. Call record, lease and sweeper.
4. Tutor client migration to WebRTC, then live validation.
5. Release.

## Handover

- Branch `f2calv/2026-11-realtime-calls` ([#29](https://github.com/f2calv/agentizr/pull/29)) holds this
  record and delivery steps 1 to 4: contracts, typed client, the `realtime` definition section, setup
  service, controller, leased call registry and unit tests. Step 3 stores the call record in
  `agent_runtime.realtime_calls` with a 20-second lease renewed every 5 seconds; ownership transitions
  are conditional updates, so concurrent replicas agree on one owner or sweeper. The runtime now runs
  two replicas with realtime calls.
- A call summary reports the end reason, duration, token usage and, when the deployment's prices are
  configured, an estimated cost. A provider close after a recorded end request is reported as
  `ClientRequested`, and the controller skips the provider hang-up once the provider has closed the
  call, because Azure does not answer it.
- The tutor client runs on Windows and Android. Its CI jobs are ready but gated until a released
  client package carries the realtime calls.
- Identity and configuration are provisioned separately: a public client registration for the tutor
  app with Windows loopback and Android application-ID redirects, the delegated `agent.realtime` scope
  on the Agentizr API, a data-plane role for Agentizr's identity on the realtime resource, the `azp`
  tenant mapping and the realtime provider.
- The .NET 11 migration branch is pending GA; this work targets the current `main`.

### Remaining Work

- Controller-level tests, the Compose integration test and the log and telemetry review above.
- Step 5: release the contracts, typed client, image and chart; then pin the client in the tutor app,
  enable its CI jobs and move the runtime deployment off Debug images.
- Public reachability for use away from the private network: expose only the realtime call routes on a
  public host with a public certificate, keep administration internal, and drop the client's trust of
  user-installed certificate authorities. Media already flows directly between the client and Azure.
- Configure per-deployment token prices so summaries carry an estimated cost.
- Persist the Windows client's token cache; Android already keeps it.

## Open Questions And Reconsideration

- Azure accepts a server-side hang-up of a WebRTC call from the controller (observed live). A hang-up
  of a call the provider already closed is not answered. Whether a non-owning replica's sweeper can
  hang up a live call without the control WebSocket is implemented but not yet observed live.
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
