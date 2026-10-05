# Copilot Instructions

## Shared Instructions

Shared Copilot instructions, skills and prompts are maintained centrally in the [.github](https://github.com/f2calv/.github) repository, under `.github/instructions/`, `.github/skills/` and `.github/prompts/`. They are deliberately not copied into this repository, so a change there takes effect everywhere without a pull request here.

To load them, clone that repository and either add it to this VS Code workspace, or link its folders into `~/.copilot/`. Its README explains both.

If those shared files are not visible, stop and tell the user rather than guessing the conventions — this repository depends on them.

Everything below is specific to this repository.

## Current Baseline

- This repository currently contains only a public .NET 10 ASP.NET Core host scaffold.
- Keep the host domain-neutral until agent behavior and protocol contracts are explicitly defined.
- Preserve the split `AppHost` bootstrap and the `WebApplicationFactory<Program>` test boundary.
- Keep Debug references pointed at the adjacent `CasCap.Common` checkout and Release references on
  centrally versioned packages.

## Boundaries

- Do not add authentication, persistence, caching, MCP, agent protocols, cloud services, frontend,
  containers, Helm, or deployment configuration without an explicit implementation request.
- Keep tracked configuration and tests credential-free and free of real account, tenant, endpoint,
  host, subscription, or resource identifiers.
- Add domain code only after its ownership boundary and public contract are established.
