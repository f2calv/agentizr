---
name: onboard-agent-runtime-app
description: 'Onboard a new application onto the Agent Runtime with its own tenant, Microsoft Entra identities, Signal chat and monitor groups, Comms host wiring, GitOps deployment and published definitions. Use when bootstrapping an application such as a new Signal-fronted assistant, or adding a tenant to an existing runtime. - Brought to you by f2calv/agentizr'
argument-hint: '[app=<name>] [tenant=<tenant-id>]'
user-invocable: true
---

# Onboard An Agent Runtime Application

Bootstrap an application that talks to users through Signal and answers through Agent Runtime
definitions. The procedure is the one used for the existing tenants: claims-derived tenancy,
separate execution and administration identities, a Comms host that owns the Signal group, and
definitions published inactive and activated separately.

Every value below in angle brackets is supplied by the caller. Never commit tenant, client,
subscription or Key Vault identifiers, account numbers or group IDs to a public repository.

## Inputs

Record these before changing anything:

| Input | Example placeholder | Notes |
| --- | --- | --- |
| Application name | `<app>` | Lower-case; names the repository, namespace and identities |
| Runtime tenant | `<tenant>` | One tenant per application isolates sessions, definitions and knowledge |
| Hosting model | own host or responder alias | Own host when the application needs its own MCP tools or endpoints |
| Chat group | `<Chat Group>` | Exact Signal group name, case and spaces included |
| Monitor group | `<Chat Group> Monitor` | Operator and gateway account only |
| Subscriber name | `<app>-comms` | Unique per gRPC subscriber |
| Providers | `EdgeGpu`, `AzureOpenAi`, ... | Each definition names one; more may be configured for comparison |
| Voice | speech-to-text and text-to-speech modes, voices, languages | Start disabled until the text path is verified |

## Procedure

Work through the steps in order. Stop for explicit user approval before step 2.

### 1. Plan

- [ ] Decide the hosting model. An own host follows the SmartHaus and CAS pattern: a Comms host
      with `AddComms(...)`, `AddCommsAgent()` and runtime authentication, plus its own MCP tools.
      A responder alias runs only the `Comms` feature from the Signalizr image and has no tools.
- [ ] Choose the tenant identifier and the agents it needs. The top-level agent is usually a
      `CommsAgent` that delegates to specialists.
- [ ] Record the plan in the operator's private tracking notes, not in this repository.

### 2. Identities (requires approval)

Run [New-AgentRuntimeIdentities.ps1](scripts/New-AgentRuntimeIdentities.ps1) for the tenant. It is
safe to re-run and performs every item below except the record keeping.

- [ ] Create `agentizr-<tenant>-exec` for the host. Grant only the `agent.execute` app role on
      the runtime API application. Authenticate with a certificate; store the combined PEM in Key
      Vault with the host's `TenantId`, `ClientId`, `Scope` and `Enabled` settings under the prefix
      the host maps onto `AgentRuntimeAzureAuthConfig`.
- [ ] Create `agentizr-<tenant>-admin` with the `agent.definition.read`, `agent.definition.publish`
      and `agent.definition.activate` app roles and no standing credential. Each publication run
      installs a short-lived in-memory certificate and removes it in a `finally` block.
- [ ] Map both client IDs to the tenant in Key Vault as
      `AgentRuntime--Agentizr--TenantAuthenticationConfig--TenantCallers--<tenant>--<n>`, with the
      execution client at `0` and the administration client at `1`.
- [ ] Record owner, expiry, rotation procedure and consumers for both applications.

### 3. Runtime configuration

- [ ] Add the tenant's providers under `CasCap:AgentRuntimeConfig:Tenants:<tenant>:Providers`.
      Non-secret settings belong in the GitOps values; API keys come from an ExternalSecret.
      There is no cross-tenant provider fallback.
- [ ] For an Azure OpenAI resource with key authentication disabled, list the provider name in
      `CasCap:AgentRuntimeConfig:Tenants:<tenant>:EntraIdProviders` instead of supplying a key, and
      confirm the runtime's identity holds an OpenAI user role on the resource. Add the resource
      origin to `CasCap:AgentDefinitionPolicyConfig:AllowedProviderAuthorities`.
- [ ] Set `CasCap:AgentRuntimeConfig:Tenants:<tenant>:TimeZoneId`.
- [ ] When the host's `/mcp` endpoint requires Basic authentication, add
      `CasCap:AgentRuntimeConfig:Tenants:<tenant>:McpCredentials:<name>` (scheme in values,
      parameter from the ExternalSecret).
- [ ] Add the host's in-cluster origin (scheme, host and port only) to
      `CasCap:AgentDefinitionPolicyConfig:AllowedMcpAuthorities`.
- [ ] Restart the runtime so the Key Vault caller mapping is loaded, then confirm an
      authenticated `GET /api/v1/agents/{name}/definitions/active` answers `404` rather than `401`.

### 4. Signal

- [ ] Create the chat and monitor groups with exact names. The monitor group contains only the
      operator and the gateway account.
- [ ] Add both names to the gateway's configured group list and redeploy it.
- [ ] Send a message from the operator account and confirm it arrives with `FromSelf=false`.

### 5. Host

For an own host:

- [ ] Start from the closest existing Comms host. Keep adjacent-checkout Debug
      `ProjectReference`s and Release `PackageReference`s, and the same branch name as the
      dependencies.
- [ ] Configure `CommsConfig` (`GroupName`, `MonitorGroupName`, subscriber name,
      `MonitorContentEnabled=false`), `AgentRuntimeClientOptions:BaseAddress` and
      `AgentRuntimeAzureAuthConfig`.
- [ ] Annotate every MCP tool with `ReadOnly`, `Destructive` and `Idempotent` hints, protect
      `/mcp` with Basic authentication and Origin rejection, and gate tools per feature.
- [ ] Return values in unambiguous units with timestamps, state sign conventions in descriptions,
      and fail a tool rather than return an empty default object.

For a responder alias, add a dormant workload alias to the Signalizr chart instead.

### 6. GitOps

- [ ] Add an Application in its own namespace and the configuration values. Hosts that load Key
      Vault through the shared edge identity read their execution identity from the step 2
      secrets; add an ExternalSecret only for values the host cannot read from Key Vault.
- [ ] Deploy through the repository's `deploy.ps1`; confirm Argo CD reports Synced and Healthy and
      the logs contain no errors.

### 7. Definitions

- [ ] Author schema-version-1 definitions without secrets. Reference providers by name and MCP
      credentials by logical name.
- [ ] Include rules that current readings always come from a tool call, never from earlier turns.
      The runtime adds the tenant-local date and time to every message, so definitions need no
      date rule unless they disable `CurrentTimeContextEnabled`.
- [ ] Publish with [Publish-AgentDefinition.ps1](scripts/Publish-AgentDefinition.ps1), run
      `-WhatIf` first, then activate. Optionally evaluate before activation.
- [ ] Read back the active versions.

### 8. Validate

- [ ] A chat message produces one reply in the chat group and diagnostics only in the monitor group.
- [ ] Every include-listed tool resolves; non-read-only tools are named explicitly.
- [ ] `agentizr_run_total` shows the new tenant, and error and fatal log counts are zero.
- [ ] No account number, group ID, credential, prompt or message body appears in logs, metrics,
      tracked configuration or definition audit metadata.

## Scripts

| Script | Purpose |
| --- | --- |
| [New-AgentRuntimeIdentities.ps1](scripts/New-AgentRuntimeIdentities.ps1) | Creates the tenant's execution and administration applications, the execution certificate and the Key Vault settings and caller mappings |
| [Publish-AgentDefinition.ps1](scripts/Publish-AgentDefinition.ps1) | Publishes definition JSON files for one tenant with an ephemeral admin certificate, then activates them |
