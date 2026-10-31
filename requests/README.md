# Agentizr Requests

Manual VS Code REST Client examples for the controller API. These collections are not automated
tests, do not start the host, and never provision identities or provider credentials.

## Setup

1. Copy [.env.example](.env.example) to `.env` in this directory only if no local file exists.
2. Set the approved HTTPS origin and tokens issued for the configured authority/audience.
   The caller must resolve to an allowed tenant through the host's trusted caller mapping.
3. Use URL-safe agent, session, snapshot and version identifiers. Set the provider/model to an
   operator-approved choice; the synthetic values do not create a working model registration.
4. Review and approve one operation's destination, identity and effects, then use **Send Request**
   for that section only. Never batch the file or automatically replay uncertain mutations.

The local `.env` is ignored. Tokens stay there, not in request bodies, browser storage or tracked
configuration. Acquire them outside these examples; there is no token endpoint or embedded client
secret. Do not weaken authorization to make an example work.

## Collections

| Collection | Surface | Required delegated scope or application role |
| --- | --- | --- |
| [runtime.http](runtime.http) | Run/stream, sessions, compaction, snapshots, overrides and instructions | `agent.execute` |
| [definitions.http](definitions.http) | Immutable publication/history, evaluation, activation/rollback and deactivation | `agent.definition.read`, `agent.definition.publish` or `agent.definition.activate`, per action |
| [document-reviews.http](document-reviews.http) | Debug-only catalogue, synchronization, source preview, immutable snapshots, chunk selection and readiness assessments | Assigned app administrator's session cookie, plus antiforgery on POST/PUT; machine scopes do not qualify |

Document-review examples require enabled administration sign-in; preview/storage also requires
existing finished review-required receipts. The catalogue lists admitted metadata, not every Blob.
Acquire the administrator cookie outside these examples. Fetch the session, retain its
antiforgery cookie and matching request token, then set the private cookie header/token variables;
never paste them into tracked files or chat. Use URL-safe app/collection/document/job/generation
identifiers. The POST source sequence and ETag must be copied as correctly escaped JSON strings from
the authorized preview. Set the generation variable from the resulting Location. Storage without
configured PostgreSQL returns 501; these endpoints are absent in Release. No manual request has
been executed as part of static example validation.

The chunk-review PUT targets an exact stored generation and sends its observed source sequence,
ETag and latest review revision (zero before the first review). It appends authenticated selection
history, preserving the snapshot; exclusions are zero-based candidate ordinals with optional
500-character reasons. A stale revision/source returns 409. GET the generation after an uncertain
outcome rather than automatically retrying. Selection never approves indexing or invokes providers.

The approval PUT requires a saved selection and its exact revision, plus the observed assessment
revision (zero initially). Classification and provenance/content/safety attestations are explicit;
false checks, missing required CEFR or no included candidates save a blocked assessment. Manuals
remain blocked pending visual associations. Selection changes require reassessment. No assessment
publishes embeddings or makes content searchable. The example deliberately withholds readiness.

The synchronization POST applies to all configured collections for the app. It persists/coalesces
requests and returns 202 with a status Location; use GET to inspect an uncertain outcome rather than
automatically resubmitting. A confirmed missing original removes stored chunks/review content,
but original blobs are unchanged. Disabled/unmapped synchronization returns 501. This operation
does not enable processing, call paid providers or make documents searchable.

Separate token variables support least-privilege identities. Publish/activate also require a
validated actor identifier. An invalid or missing identity is not a resource-not-found result;
distinguish 401/403 from 404. Tenancy and audit actors come from validated claims, never JSON fields.

Definition writes require PostgreSQL; configuration-backed definitions are read-only and return
501 for unsupported writes. The publication example uses a secret-free OpenAI definition with no
tools: configure the approved provider's credentials server-side before activation/evaluation.
Publication returns 201 with a version Location and does not activate it. Inspect that version
with the separate read request. Versions are immutable; do not reuse an existing version for edits.

Evaluation and activation with evaluation can call a real model, despite fixture-backed tools.
The greeting scenario is illustrative, not a meaningful production quality gate. Existing
provider/tool allowlists, dependencies and evaluation policy remain enforced.

Runtime runs can execute the configured agent's tools regardless of an input asking not to.
Streams are incremental JSON arrays, not SSE. An interrupted model/tool run must not be blindly
retried; multi-replica coordination does not guarantee exactly-once external effects.

Success responses use JSON except empty 204 mutations. Automatic validation errors and session-busy
409 responses use Problem Details. Requests containing deliberate failures are titled accordingly.
Manual/static inspection does not establish any live response or authentication behavior.

See the [HTTP contract](../src/CasCap.Backend/README.md) and
[HA failure boundaries](../docs/high-availability.md) before using mutations.
