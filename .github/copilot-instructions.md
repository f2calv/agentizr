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
- Use Redis only for secret-free definition caching and version-qualified runtime state. Provider
  credentials remain behind `IProviderCredentialStore` and must not enter PostgreSQL or Redis.
- Apply EF migrations externally. Never use `HasData` for runtime definitions or other mutable,
  tenant-owned data.

## Boundaries

- Do not add frontend, containers, Helm, deployment configuration, or new cloud-service ownership
  without an explicit implementation request.
- Keep tracked configuration and tests credential-free and free of real account, tenant, endpoint,
  host, subscription, or resource identifiers.
- Add domain code only after its ownership boundary and public contract are established.
