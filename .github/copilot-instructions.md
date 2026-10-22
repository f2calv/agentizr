# Copilot Instructions

## Shared Instructions

Shared Copilot instructions, skills and prompts are maintained centrally in the [.github](https://github.com/f2calv/.github) repository, under `.github/instructions/`, `.github/skills/` and `.github/prompts/`. They are deliberately not copied into this repository, so a change there takes effect everywhere without a pull request here.

To load them, clone that repository and either add it to this VS Code workspace, or link its folders into `~/.copilot/`. Its README explains both.

If those shared files are not visible, stop and tell the user rather than guessing the conventions — this repository depends on them.

Everything below is specific to this repository.

## Current Baseline

- This repository owns a public .NET 10 multi-tenant Agent Runtime, its versioned contracts, and its
  typed client.
- Preserve the split `AppHost` bootstrap and the `WebApplicationFactory<Program>` test boundary.
- Keep Debug references pointed at the adjacent `CasCap.Common` checkout and Release references on
  centrally versioned packages.
- Derive tenant identity from validated authentication claims; never accept it in runtime request
  DTOs.
- Keep PostgreSQL as the definition authority. Store immutable schema-versioned JSONB snapshots and
  select the active version through a separate tenant-and-agent-qualified pointer.
- Keep publication and activation separate: publication appends an inactive immutable snapshot;
  activation alone advances the pointer and appends actor/reason audit history.
- Protect definition reads, publication and activation with separate scopes. Derive tenant,
  publisher and activation actor identity from validated claims, never request DTOs.
- Use Redis only for definition caching and revision-qualified durable-state payload caching. Provider
  and MCP credentials remain behind `IProviderCredentialStore` / `IMcpCredentialStore` and must not
  enter PostgreSQL or Redis.
- PostgreSQL owns sessions, named snapshots, overrides and opt-in debugging history. Check the
  durable state revision before using a cached payload; old revisions expire without invalidation.
  Active definition pointers may be cached briefly with absolute expiry and activation invalidation.
  Runtime mutations must hold `IAgentSessionLock` and write using its owning PostgreSQL connection.
  Never substitute cache-fill locking or local-cache invalidation for session coordination.
- Keep debugging history separate from resumable state; reset and compaction must not remove it.
  Persist only explicit message/tool/usage projections, never credentials, headers or raw framework
  results. Use the Compose tests profile and isolated per-run databases/key namespaces for local
  persistence integration tests.
- Apply EF migrations externally. Never use `HasData` for runtime definitions or other mutable,
  tenant-owned data.

## Boundaries

- CI publishes the NuGet packages, the Release image and the application chart. Do not add
  frontend, new deployment targets, or new cloud-service ownership without an explicit
  implementation request.
- Keep tracked configuration and tests credential-free and free of real account, tenant, endpoint,
  host, subscription, or resource identifiers.
- Add domain code only after its ownership boundary and public contract are established.
