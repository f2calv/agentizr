# Debug Runtime Logging Editor

The existing Blazor WebAssembly frontend embeds the source-only CasCap.Common typed editor at
`/runtime-configuration`. It manages only the host logging minimum, not tenant definitions,
credentials, sinks, category overrides or arbitrary settings.

## Source and Deployment Boundary

The [client project](../src/CasCap.App.Client/CasCap.App.Client.csproj) references the adjacent
Common RCL in Debug. The [server project](../src/CasCap.App.Server/CasCap.App.Server.csproj)
references the Common management API alongside the existing PostgreSQL reader.
The [page](../src/CasCap.App.Client/Pages/RuntimeLoggingSettings.razor) supplies a typed enum field;
it is not a generated key/string form or a dynamically loaded plugin.

The [shared logging DTO](../src/CasCap.AgentRuntime.Contracts/RuntimeConfiguration/RuntimeLoggingControls.cs)
contains a browser-safe enum with the existing string JSON representation. No Serilog, EF, database
driver or server configuration dependency enters the downloaded contracts/client.

Release excludes the page, client registration, management endpoints and reader until the Common
packages are published and deliberately adopted. The existing [deployment entry point](../deploy.ps1)
continues to build Debug from sibling project references. An image build/deployment is not a
configuration publication; simply opening this page never writes a revision.

## Configuration Examples

The existing [reader bootstrap](runtime-configuration-soak.md#configuration-examples) must be enabled
with a required initial publication. Administration must use its existing OIDC/HttpOnly-cookie
sign-in. Management defaults off and has no anonymous Development bypass.

Prefer a separate explicit operator allowlist, using the validated claim selected by
`AdministrationConfig.AdministratorClaimType`:

```json
{
  "CasCap": {
    "RuntimeConfigurationConfig": {
      "ManagementEnabled": true,
      "ManagementOperators": ["00000000-0000-0000-0000-000000000000"]
    }
  }
}
```

For an explicitly approved Debug trial only, the operator may instead grant all existing configured
app-administrator identities host-wide logging access:

```json
{
  "CasCap": {
    "RuntimeConfigurationConfig": {
      "ManagementEnabled": true,
      "ManagementUseAppAdministratorAssignments": true
    }
  }
}
```

This is a deliberate privilege expansion: a tenant administrator otherwise has no right to alter
the host's logging. The two grant modes are exclusive. Keep real identifiers in the existing private
configuration chain, never tracked examples. Machine bearer tokens never grant editor access.

## Browser and API Flow

1. Follow **Runtime logging** from the Debug app bar and sign in through the existing administration
   flow if necessary.
2. The host authorizes `GET /admin/api/runtime-configuration` before returning its fixed
   application/environment/contract destination. The browser cannot select an arbitrary target.
3. The RCL reads the durable logging document from the Common management API, including its exact
   64-bit revision and expiry. An expired head may show a different value from the current reader.
4. Select a typed minimum level and preview it. Preview validates without publishing.
5. Only an explicit reason, optional temporary lifetime and destination confirmation permit
   publication. Every POST obtains a fresh principal-bound antiforgery token; the browser retains
   the paired HttpOnly cookie. Neither client automatically retries a mutation.
6. Durable commit is shown as **activation pending**, not acknowledgement from all replicas.
   Temporary expiry returns to the immutable startup baseline, not the previous journal revision.

The [server wiring](../src/CasCap.Backend/Extensions/WebApplicationBuilderExtensions.RuntimeConfiguration.Management.cs) uses the
existing named administration cookie for all three Common view/edit/publish policies. Unknown
identities receive 403, absent cookie authentication receives 401, and an unavailable authority does
not reset the accepted runtime value. The optional bootstrap controller is absent when disabled.
Shared PostgreSQL Data Protection keys and the host's generated stylesheet bundle remain unchanged.

## Verification

The focused `*RuntimeLogging*` suite exercises:

- compatible string serialization and browser-neutral contracts;
- actual host-page/RCL rendering, typed fields, exact long revisions and read-only initial loading;
- unauthorized, forbidden, disabled and unavailable page states;
- explicit operator grants and rejection of ambiguous/missing grant configuration;
- cookie-scheme enforcement against machine authentication, fixed scope and JSON antiforgery;
- preview without append, publication through the authoritative provider and actual reader switch;
- existing reload, startup-baseline expiry, retained cache and logging forwarding behavior.

Logging tests own isolated databases and apply only real Common configuration migrations, not
unrelated application/vector schema migrations. Tests never publish to a deployed authority.
Physical browser loading and post-rollout authentication/assets/health must be verified separately;
local rendering tests alone do not establish that a deployed browser session works.

Observed local result: all 28 focused cases pass, with zero failures/skips. The complete Debug
solution and Release WASM client build with zero warnings/errors. The full Release host currently
remains blocked by separate, unpublished shared exit-state APIs; the Debug editor introduces no
Release reference to the Common management/RCL packages.
