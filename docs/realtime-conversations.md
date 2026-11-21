# Resumable Voice Conversations

Realtime calls and resumable conversations have different lifetimes. A call owns a WebRTC audio
connection, tools, usage and a hard duration ceiling. An optional conversation retains only
bounded text context so a later call can continue the discussion with the same learner.
The [realtime architecture decision](adr-0002-realtime-voice-calls.md) owns call brokering.

## Contract

`GET /api/v1/agents/{agentName}/realtime/capabilities` uses the existing `agent.realtime`
policy. It reports `voiceEnabled`, `knowledgeToolsEnabled` and `retentionAllowed` for the
active definition in the caller's tenant, or 404 if there is no active definition. A known agent
without voice returns all three flags as false. The response is not cacheable and the check does
not start a call or invoke any provider or tool. The flags describe definition policy, not provider
readiness, knowledge-search authorization, durable storage, or a specific conversation's eligibility.

`POST /api/v1/agents/{agentName}/realtime/calls` accepts the existing `sdpOffer`, an opt-in
`retainConversation` boolean (default false) and an optional `conversationId` GUID from an earlier
response. A retained new call returns a new conversation reference; Resume submits that reference
with retention enabled. Call IDs remain distinct and continue to identify hang-up requests.

The endpoint returns an explicit 409 for unavailable retention, missing/expired/foreign history,
definition changes, overlapping use or a conversation reference submitted without retention.
Ordinary calls retain their existing contract, including 403 for a private knowledge call without
its additional search grant. Retention requires PostgreSQL plus authenticated issuer and user claims;
the in-memory development adapter does not pretend to provide durable resumption.

Retained setup waits up to twenty seconds for `session.updated` and acknowledgement of every
replayed item before returning SDP. Provider rejection, control loss or a readiness timeout ends
the new call and reports failure, never success with silently missing history.

The typed client's `CreateRealtimeCallAsync` has optional `conversationId` and
`retainConversation` arguments after its existing cancellation token. Real SDP offers must come
from the client's media engine; a standalone REST-client example is intentionally deferred rather
than documenting a synthetic offer as a successful call.

## Storage And Isolation

Voice projections reuse `agent_runtime.agent_states`, its revision mechanism and its exact
connection-bound PostgreSQL session lock. They use a separate `voice` key namespace and schema
version, never the text-session codec, snapshots or text history.

- Tenant and caller identity come from validated authentication claims, not request DTOs.
- The opaque reference is further qualified by issuer, subject and agent. Possession of another
  user's reference does not authorize reading or resuming their conversation.
- One call holds the conversation lock for its lifetime, preventing two replicas from extending
  the same conversation concurrently. This uses one database connection per retained active call;
  include those connections in runtime capacity and pool sizing.
- A call-owned scope captures the tenant before the HTTP setup request ends.
- Replay is refused after a definition version change; current instructions and allowlists cannot
  accidentally inherit incompatible previous context.
- Voice reads bypass Redis, so expiry does not leave a cached transcript projection behind.

No schema migration is required. Existing call records continue to store usage and summaries,
not conversation text.

## Retention And Replay

Retention is opt-in. The projection holds at most 40 utterances and 32,768 text characters, removing
oldest whole utterances when either bound is exceeded. A single utterance over 16,384 characters
fails the retained call rather than silently splitting it. Input/output transcription is text
context, not an exact representation of all audio.

Message creation events preserve order even when learner transcription completes after a tutor
answer. Repeated item IDs update the same utterance. Interrupted assistant utterances are
discarded, including later transcript events for the same item. Replay creates only user and
assistant messages; functions, tool results and credentials are never replayed or re-executed.
GA assistant text uses `output_text`, as specified by the
[Azure migration guide](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/realtime-audio-preview-api-migration-guide).

Expiry is 24 hours from the last retained utterance or interruption; merely reopening a conversation
does not extend it. Expired history is immediately unusable. The existing call-maintenance service
checks live storage once per minute and deletes expired voice rows in batches of at most 100,
skipping rows held by an active owner. A stopped service, database outage or backlog delays physical
deletion, not the resume expiry check. Backups follow the operator's separate backup retention.
Do not treat this feature as an unlimited transcript archive.

## Knowledge Boundary

Only calls bound to an approved `PublicReference` collection can combine document knowledge with
opt-in retention. Private or unclassified collections remain ineligible: their text could repeat
evidence that requires a separate grant. A retained public-reference answer can repeat approved
material, but raw passages and tool payloads are not retained.

The retained conversation records an identity tied to the tenant/agent binding, collection policy
revision and embedding profile. Resume rejects a different identity. The runtime rechecks current
policy before replay and at conversation events, and ends the call on a detected change. A newer
persisted indexing policy also invalidates the earlier one. These are checks at event boundaries,
not instantaneous revocation of text already sent to the voice provider. To invalidate replay of
withdrawn public-reference material, advance the collection policy revision; deleting a source alone
does not remove words already present in a retained transcript.

## Verification

- `RealtimeConversationTests`: offline order, deduplication, bounded context, interruption removal,
  serialization and authenticated-caller key separation.
- `RealtimeConversationStoreTests`: isolated PostgreSQL persistence across scopes, tenant/agent/
  user isolation, connection-bound overlap refusal, definition mismatch, exact expiry and cleanup;
  a scripted WebSocket verifies provider acknowledgement and history rejection.
- `RealtimeCallProtocolTests`: HTTP 409 before provider contact for unavailable retention, plus
  existing ordinary-call and knowledge-permission behaviour.

These are not live-model recall or mobile background-audio acceptance. Publish the changed
contracts/client and deploy the matching runtime before upgrading a Release consumer.
