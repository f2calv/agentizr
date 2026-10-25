# Runtime Configuration Logging Soak

This opt-in Debug slice exercises the unpublished CasCap.Common runtime/PostgreSQL libraries
through agentizr's actual Serilog pipeline. Immutable deployment settings, sinks, credentials,
category overrides and tenant definitions remain outside the runtime contract.

## Build and Deployment Boundary

The thin [entrypoint](../src/CasCap.App.Server/Program.cs) delegates to
[AppHost](../src/CasCap.App.Server/AppHost.cs). The [runtime registration](../src/CasCap.App.Server/AppHost.RuntimeConfiguration.cs)
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
existing application database; the independently migrated `runtime_configuration` schema owns
only configuration records. Supplying the same connection does not merge contexts or migrations.

Optional `RefreshIntervalMs` defaults to 5000, with supported values from 100 to 3600000.
The startup baseline is the deployment's Serilog default minimum; category overrides stay unchanged.
The [typed contract](../src/CasCap.App.Server/RuntimeConfiguration/Models/RuntimeLoggingControls.cs)
contains only `MinimumLevel`, using a defined Serilog level such as `"Information"` or `"Debug"`.
The [monitor consumer](../src/CasCap.App.Server/RuntimeConfiguration/Services/RuntimeLoggingService.cs)
owns a disposable subscription and updates a per-host pipeline switch, not the console-only switch.

## Provision Before Enabling

Confirm the exact database and application/environment scope with the operator before any live
migration, SQL write, restart or deployment. Keep migration, operator and SELECT-only runtime
permissions separate; prevent runtime identities from editing/deleting journal entries.

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
INSERT INTO runtime_configuration.revisions
    (application, environment, contract_id, revision, contract_version, payload,
     recorded_at_utc, actor, reason)
VALUES
    ('example-application', 'example-environment', 'logging', 1, 1,
     '{"MinimumLevel":"Information"}'::jsonb, now(), 'example-operator', 'Initial logging baseline');
INSERT INTO runtime_configuration.heads (application, environment, contract_id, revision)
VALUES ('example-application', 'example-environment', 'logging', 1);
COMMIT;
```

A pre-existing initial head conflicts rather than being overwritten. Do not use an upsert that
rewrites history. Missing or unavailable initial configuration fails enabled-host startup.
Direct SQL is an operator-only first-soak path; readers still enforce DTO shape and validation,
but SQL itself does not run the library's typed publication validators.

## Temporary Change with Optimistic Concurrency

This example requires revision 1 and publishes revision 2 with a fifteen-minute expiry. Change the
expected revision deliberately for each later publication. A stale request must fail, not silently
select a newer revision.

```sql
BEGIN;
DO $publication$
DECLARE
    current_revision bigint;
BEGIN
    SELECT revision INTO current_revision
    FROM runtime_configuration.heads
    WHERE application = 'example-application'
      AND environment = 'example-environment' AND contract_id = 'logging'
    FOR UPDATE;
    IF NOT FOUND OR current_revision <> 1 THEN
        RAISE EXCEPTION 'Configuration revision conflict';
    END IF;
    INSERT INTO runtime_configuration.revisions
        (application, environment, contract_id, revision, contract_version, payload,
         recorded_at_utc, actor, reason, expires_at_utc)
    VALUES
        ('example-application', 'example-environment', 'logging', 2, 1,
         '{"MinimumLevel":"Debug"}'::jsonb, now(), 'example-operator',
         'Temporary logging soak', now() + interval '15 minutes');
    UPDATE runtime_configuration.heads SET revision = 2
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

## Acceptance and Live Soak Checklist

- [x] Debug build uses local source references; normal Release build still compiles.
- [x] Real PostgreSQL publication changes the effective pipeline minimum, verified by emitted events
  and forwarding to an additional Microsoft logging provider.
- [x] Deployment-owned category overrides and the same process-memory cache sentinel are preserved.
- [x] Existing endpoint smoke tests pass while the feature is disabled.
- [ ] Confirm the live target, scope, migration/publication credentials and rollout authorization.
- [ ] Apply the owned migration and valid initial snapshot before enabling the host.
- [ ] Deploy the Debug image through the normal deployment shim and record its immutable digest.
- [ ] Publish a temporary level change through the selected PostgreSQL MCP connection.
- [ ] Observe application debug events in the real console/export destination, not just a DTO.
- [ ] Record replica convergence, unchanged host identity/cache behavior and eventual expiry.
- [ ] Exercise invalid-candidate and dependency-outage behavior in an approved isolated target.

Local tests use the Compose `tests` service and isolated per-fixture databases. They do not prove
the live exporter, Kubernetes replica convergence, pod replacement, a database outage deadline
or a completed soak. Runtime-reader errors contain sanitized failure types, not configuration
payloads; inspect rejected records only through an authorized operator connection.
