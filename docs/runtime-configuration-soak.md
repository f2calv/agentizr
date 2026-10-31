# Runtime Configuration Logging Soak

This opt-in Debug slice exercises the unpublished CasCap.Common runtime/PostgreSQL libraries
through agentizr's actual Serilog pipeline. Immutable deployment settings, sinks, credentials,
category overrides and tenant definitions remain outside the runtime contract.

The Debug frontend now embeds an optional [typed logging editor](runtime-configuration-management.md).
It requires separate explicit host-wide operator authorization; opening it never publishes.

## Build and Deployment Boundary

The thin [entrypoint](../src/CasCap.App.Server/Program.cs) delegates to
[AppHost](../src/CasCap.App.Server/AppHost.cs). The [runtime registration](../src/CasCap.Backend/Extensions/WebApplicationBuilderExtensions.RuntimeConfiguration.cs)
uses sibling project references in Debug; [Dockerfile.Debug](../Dockerfile.Debug) copies those
dependencies into its build context.

[deploy.ps1](../deploy.ps1) already delegates to the centralized deployment workflow, which invokes
the build shim with `-Push -Configuration Debug`. No script change or package publication is needed
for this development path. Running it still publishes an image and changes GitOps deployment state;
do not run it merely to validate this document.

The new code is deliberately excluded from Release until the runtime, PostgreSQL and updated hosting
packages have been published and adopted together. Release rejects an enabled soak instead of
silently ignoring it. Neither a local successful test nor a Debug build is evidence of a live rollout.

## Configuration Examples

Provide bootstrap settings through the existing deployment configuration/Key Vault chain:

```json
{
  "CasCap": {
    "RuntimeConfigurationConfig": {
      "Enabled": true,
      "Application": "example-application",
      "ConnectionString": "<supplied by the deployment secret store>"
    }
  }
}
```

`Enabled` defaults to false. The application name is a required durable scope identifier, not a tenant
identifier. The environment is the host's actual .NET environment name, including any custom name.
The contract identifier is `logging`, version 1. The PostgreSQL target may be standalone or the
existing application database; the independently migrated `cascap_config` schema owns
only configuration records. Supplying the same connection does not merge contexts or migrations.

For explicitly approved reuse of the existing application account, omit the independent connection
and set `ApplicationDatabaseReuseEnabled` to `true`. This uses the same normalized application
connection as persistence, including `DatabaseConfig.DatabaseName`; copying the original secret
could otherwise select a different database. Both connection modes selected, neither selected,
or a missing application connection fail startup. Never duplicate the resolved connection into
tracked settings.

```json
{
  "CasCap": {
    "RuntimeConfigurationConfig": {
      "Enabled": true,
      "Application": "example-application",
      "ApplicationDatabaseReuseEnabled": true,
      "RefreshIntervalMs": 5000
    }
  }
}
```

Optional `RefreshIntervalMs` defaults to 5000, with supported values from 100 to 3600000.
The startup baseline is the deployment's Serilog default minimum; category overrides stay unchanged.
The [typed contract](../src/CasCap.AgentRuntime.Contracts/RuntimeConfiguration/RuntimeLoggingControls.cs)
contains only `MinimumLevel`, using a defined Serilog level such as `"Information"` or `"Debug"`.
The [monitor consumer](../src/CasCap.Backend/RuntimeConfiguration/Services/RuntimeLoggingService.cs)
owns a disposable subscription and updates a per-host pipeline switch, not the console-only switch.

## Provision Before Enabling

Confirm the exact database and application/environment scope with the operator before any live
migration, SQL write, restart or deployment. Keep migration, operator and SELECT-only runtime
permissions separate; prevent runtime identities from editing/deleting journal entries.
The approved first-soak exception may reuse the existing application account after schema and
initial-revision verification. That account retains its application privileges: this is a shared-
privilege tradeoff, not SELECT-only enforcement or a new credential. Least-privilege separation
remains the production recommendation.

Apply the configuration context's reviewed migration externally. From the application repository,
using a migration connection already supplied securely in the current process:

```powershell
dotnet ef database update --project ..\CasCap.Common\src\CasCap.Common.Configuration.PostgreSql --configuration Release --context RuntimeConfigurationDbContext --connection "$env:RUNTIME_CONFIGURATION_MIGRATION_CONNECTION"
```

This command is an operator action, not application startup. Review the migration SQL beforehand.
Do not print the connection value or copy it into tracked configuration. The existing agent-runtime
migrator does not apply this independently owned context.

## Initial Publication

Before enabling the reader, publish a valid initial snapshot. These SQL examples are templates:
replace the synthetic scope and actor with operator-approved values through the selected database
connection. Both journal and head must commit together.

```sql
BEGIN;
INSERT INTO cascap_config.revisions
    (application, environment, contract_id, revision, contract_version, payload,
     recorded_at_utc, actor, reason)
VALUES
    ('example-application', 'example-environment', 'logging', 1, 1,
     '{"MinimumLevel":"Information"}'::jsonb, now(), 'example-operator', 'Initial logging baseline');
INSERT INTO cascap_config.heads (application, environment, contract_id, revision)
VALUES ('example-application', 'example-environment', 'logging', 1);
COMMIT;
```

A pre-existing initial head conflicts rather than being overwritten. Do not use an upsert that
rewrites history. Missing or unavailable initial configuration fails enabled-host startup.
Direct SQL is an operator-only first-soak path; readers still enforce DTO shape and validation,
but SQL itself does not run the library's typed publication validators.

## Temporary Change with Optimistic Concurrency

This example requires a verified Information startup baseline and revision 1, and publishes revision
2 with a fifteen-minute Debug expiry. For a verified Debug startup baseline, use Information instead.
Other baselines require explicit confirmation of a distinct safe temporary level. Change the
expected revision deliberately for each later publication. A stale request must fail, not silently
select a newer revision.

```sql
BEGIN;
DO $publication$
DECLARE
    current_revision bigint;
BEGIN
    SELECT revision INTO current_revision
    FROM cascap_config.heads
    WHERE application = 'example-application'
      AND environment = 'example-environment' AND contract_id = 'logging'
    FOR UPDATE;
    IF NOT FOUND OR current_revision <> 1 THEN
        RAISE EXCEPTION 'Configuration revision conflict';
    END IF;
    INSERT INTO cascap_config.revisions
        (application, environment, contract_id, revision, contract_version, payload,
         recorded_at_utc, actor, reason, expires_at_utc)
    VALUES
        ('example-application', 'example-environment', 'logging', 2, 1,
         '{"MinimumLevel":"Debug"}'::jsonb, now(), 'example-operator',
         'Temporary logging soak', now() + interval '15 minutes');
    UPDATE cascap_config.heads SET revision = 2
    WHERE application = 'example-application'
      AND environment = 'example-environment' AND contract_id = 'logging';
END;
$publication$;
COMMIT;
```

Expiry restores the immutable startup baseline without rewriting the head or historical record.
An already accepted expiry is also enforced during rejected refreshes. Poll scheduling and in-flight
database I/O can delay reconciliation; it is not a hard real-time safety deadline.

To restore earlier content manually before expiry, append another full snapshot at the next revision,
set `restored_from_revision` to the historical source and advance the head in the same checked
transaction. Never move the head backward or UPDATE an existing journal payload.

## Payload-Free Replica Evidence

Only an enabled Debug soak adds `runtimeLogging` to the existing `/status` response. Disabled and
Release responses retain their existing shape. Retain the deployment's existing ingress/access
controls. Query each replica directly through an approved operator path rather than assuming
load-balanced requests reach both.

The [probe](../src/CasCap.Backend/RuntimeConfiguration/Services/RuntimeLoggingProbe.cs) reports a
random process identity, immutable startup minimum, current actual pipeline switch minimum,
actual Debug acceptance for the probe category, monitor apply count and last-apply UTC time.
It emits no log event and includes no connection, revision payload, user or document content;
therefore restrictive log levels cannot hide its evidence. Category overrides remain effective.

The startup minimum is not necessarily the initial durable revision. Expiry returns to startup,
not to revision one. The probe does not expose accepted durable revision/expiry metadata. If
temporary Debug equals the startup baseline, expiry leaves the effective controls unchanged and
the Common monitor need not notify. Unchanged status or apply counts cannot prove that expiry
was reconciled. Use the approved distinct-level procedure instead; no Common diagnostics API
change is required:

1. After the coordinated rollout is healthy, record the immutable image digest and each replica's
   pod identity, probe process identity, startup minimum and current effective minimum.
2. Verify both startup baselines agree. For Debug use temporary Information; for Information use
   temporary Debug. Any other baseline or disagreement requires operator confirmation before
   publication. Keep revision 2 blocked until rollout readiness and level selection are established.
3. The designated publisher appends the checked temporary revision with the operator-approved
   expiry window. The completed short activation/expiry check used two minutes, superseding the
   earlier fifteen-minute proposal; it was not a sustained soak.
   Record its revision, selected level and expiry through the authorized operator connection.
4. Capture each replica's actual switch transition and corresponding Debug acceptance. When its
   current minimum already equals the temporary level, record that fact rather than claiming an
   activation transition; a fresh before/after transition needs a separately approved setup.
5. After expiry, verify return to the recorded startup minimum and corresponding Debug acceptance
   on the same pod and probe process identities. Confirm no intervening publication or deployment
   configuration change occurred. Restarted/replaced replicas do not establish same-process expiry.

This demonstrates bounded effective behavior, not private accepted-revision/fallback status or exact
internal reconciliation timing. Do not infer expiry solely from elapsed time or apply counts.

## Acceptance and Live Check Checklist

The first six items below describe isolated local validation, not live exporter or cache evidence.

- [x] Debug build uses local source references; normal Release build still compiles.
- [x] Real PostgreSQL publication changes the effective pipeline minimum, verified by emitted events
  and forwarding to an additional Microsoft logging provider.
- [x] Deployment-owned category overrides and the same process-memory cache sentinel are preserved.
- [x] Existing endpoint smoke tests pass while the feature is disabled.
- [x] Focused bootstrap tests reject missing/ambiguous connections and preserve disabled status shape.
- [x] Isolated PostgreSQL tests prove the effective database override, actual-switch probe and expiry
  to Information or Debug startup baselines, independently of revision one's Warning value.
- [x] Confirm the live target, scope, migration/publication credentials and rollout authorization.
- [x] Apply the owned migration and valid initial snapshot before enabling the host.
- [x] Deploy the Debug image through the normal deployment shim and record its immutable digest.
- [x] Verify immutable startup baselines and select the approved distinct temporary level.
- [x] Publish the checked two-minute revision through the designated operator session.
- [x] Capture actual switch/Debug-filter evidence per replica before, during and after expiry.
- [x] Confirm return to startup on unchanged pod/process identities, with no intervening publication
  or deployment configuration change.
- [ ] Exercise invalid-candidate and dependency-outage behavior in an approved isolated target.

The completed two-replica live check verified Information startup and effective baselines, then
Debug with filter acceptance and apply count 2, followed by automatic return to Information with
Debug rejected and apply count 3. Reported application times were approximately 0.74 and 2.63 seconds
after publication, and 0.74 and 2.62 seconds after expiry. These are probe-reported switch application
times, not measurements of private reader reconciliation.

Both replicas retained their exact pod/process identities and immutable image, stayed Ready with
zero restarts and returned HTTP 200 from health checks. The designated publisher confirmed head 2
and exactly the two preserved journal revisions, with no later publication. Deployment/configuration
freeze held through the final observation; task-owned loopback helpers were stopped. The freeze
is released. The provider remains enabled with the expired head retained; no reset publication
is required. Private deployment identities and detailed evidence remain outside this public guide.

This short check does not prove emitted log delivery, live cache retention, private accepted-revision
or fallback metadata, exact internal timing, or sustained stability. Outage testing was not authorized
or performed. Any future publication or deployment requires its own applicable authorization.

Local tests use the Compose `tests` service and isolated per-fixture databases. They do not prove
the live exporter, Kubernetes replica convergence, pod replacement, a database outage deadline
or a sustained soak. Runtime-reader errors contain sanitized failure types, not configuration
payloads; inspect rejected records only through an authorized operator connection.
